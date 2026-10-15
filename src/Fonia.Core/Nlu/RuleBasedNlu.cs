using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Phraseology;

namespace Fonia.Core.Nlu;

/// <summary>
/// Understands what the pilot said, without rules about how they must say it: it looks for the callsign, the
/// request words and the data (runway, parking position) wherever they are, and accepts "bom dia", "ok", extra words,
/// a different order and an abbreviated callsign. It never decides the ATC answer. When it cannot find anything it
/// returns <see cref="Unknown"/>, and the controller asks the pilot to say again.
/// </summary>
public sealed class RuleBasedNlu : INlu
{
    private const int LongestCallsignInTokens = 12;
    private const int GluedStemLength = 5;

    private static readonly string[] SayAgainWords =
        ["repita", "repete", "repetir", "repeti", "novamente", "denovo", "copiei", "entendi", "confirme", "como"];

    // The pilot is asking for something ("pronto" alone is a request too, unless the pilot is repeating "chame pronto").
    private static readonly string[] RequestStems =
        ["solicit", "pedi", "pedido", "gostaria", "gostariamos", "preciso", "posso", "quero", "instruco", "instrucao"];

    // The pilot is repeating what the controller said, or acknowledging it.
    private static readonly string[] ReadbackStems =
        ["autorizad", "aprovad", "ciente", "cient", "sient", "copiad", "coteja", "chame", "chamo", "chamar", "chamand", "report", "repart", "portar", "aguardo", "aguardando", "mantenho", "afirmo", "negativo"];

    private static readonly string[] PushbackStems = ["pushback", "push", "empurr", "recu"];
    // "cionament" because the recognizer glues "solicito acionamento" into "solicitocionamento".
    private static readonly string[] StartupStems = ["acion", "cionament", "partida", "start", "ligar", "ligand", "motor"];
    private static readonly string[] TaxiStems = ["tax"];

    // The departure (MCA 100-16, Art. 126). "partida" is also the engine start of the Solo, so it counts only after the taxi.
    // "colag" because the recognizer often writes "de colagem" in two words.
    private static readonly string[] DepartureStems = ["decol", "colag"];
    private static readonly string[] LinedUpStems = ["alinhad", "alinhei", "alinad"];
    private static readonly string[] LineUpStems = ["alinh"];

    // The arrival (MCA 100-16, Art. 128 to 132, 136).
    private static readonly string[] LandingStems = ["pouso", "pousar", "circuito", "aproxim"];
    private static readonly string[] GoAroundStems = ["arremet", "perdida"];

    // A pilot who only says "pronto" is ready for the taxi: it is what the controller asks for after startup and pushback.
    private static readonly string[] ReadyStems = ["pronto", "pronta"];
    private static readonly string[] StandWords = ["box", "pos", "posicao", "stand", "patio", "vaga", "gate"];

    public Intent Interpret(string? text, FlightContext flight)
    {
        ArgumentNullException.ThrowIfNull(flight);

        var tokens = SpokenTokens.Tokenize(text);

        if (tokens.Count == 0)
        {
            return new Unknown(text ?? string.Empty);
        }

        var rest = RemoveCallsign(tokens, flight.Callsign, out var mentioned);
        var callsign = mentioned ? flight.Callsign : null;

        if (rest.Any(t => SayAgainWords.Contains(t, StringComparer.Ordinal)) && !HasAny(rest, PushbackStems, StartupStems, TaxiStems))
        {
            return new SayAgain(callsign);
        }

        var pushback = HasAny(rest, PushbackStems);
        var startup = HasAny(rest, StartupStems);
        // "ciente" comes out of the recognizer as "sente" and "siente" too; the short one is no stem because it is inside other words.
        var repeating = HasAny(rest, ReadbackStems) || rest.Contains("sente");

        var departure = DepartureIntent(rest, flight, repeating, callsign);

        if (departure is not null)
        {
            return departure;
        }

        var arrival = ArrivalIntent(rest, flight, repeating, callsign);

        if (arrival is not null)
        {
            return arrival;
        }

        // "no ponto de espera da 15" is the pilot reporting where the aircraft is, which is not a request ("pronto" in it
        // is not "pronto para o taxi") and not the readback of the taxi clearance (which says "para o ponto de espera").
        if (!repeating && !HasAny(rest, RequestStems) && IsHoldingShortReport(rest))
        {
            return new ReportHoldingShort(callsign, FindRunwayInReadback(rest));
        }

        var taxi = HasAny(rest, TaxiStems) || (HasAny(rest, ReadyStems) && !pushback && !startup && !repeating);
        var asking = HasAny(rest, RequestStems) || (HasAny(rest, ReadyStems) && !repeating);

        if (!pushback && !startup && !taxi)
        {
            // Only the callsign and who is being called, with no numbers or readback words: not an answer.
            if (mentioned && !repeating && !asking && !rest.Contains("pista") && !HasNumber(rest))
            {
                return new CallsignOnly(callsign);
            }

            // Said something, but no request: with an instruction waiting, it is the pilot answering it, if it sounds
            // like an answer (a readback word, a runway or numbers); anything else is not understood.
            var soundsLikeAnAnswer = repeating || rest.Contains("pista") || HasNumber(rest);

            return flight.PendingReadback is not null && soundsLikeAnAnswer
                ? new Readback(callsign, string.Join(' ', rest))
                : new Unknown(text ?? string.Empty);
        }

        if (repeating && !asking || (!asking && Answers(flight.PendingReadback, pushback, startup, taxi)))
        {
            return new Readback(callsign, string.Join(' ', rest));
        }

        if (pushback)
        {
            return new RequestPushback(callsign, startup, FindStand(rest));
        }

        if (startup)
        {
            return new RequestStartup(callsign, FindStand(rest));
        }

        return new RequestTaxi(callsign, FindRunway(rest));
    }

    // What the pilot says at the holding point and on the runway: ready for departure, lined up, taking off. A readback (which
    // has words like "autorizado") is none of these, and neither is the repeating of the line-up or the takeoff clearance.
    private static Intent? DepartureIntent(List<string> rest, FlightContext flight, bool repeating, string? callsign)
    {
        if (repeating)
        {
            return null;
        }

        if (HasAny(rest, ["decolando", "decolei"]) || (rest.Contains("iniciando") && HasAny(rest, DepartureStems)))
        {
            return new ReportTakingOff(callsign);
        }

        if (HasAny(rest, LinedUpStems))
        {
            return new ReportLinedUp(callsign, FindRunwayInReadback(rest));
        }

        var wantsDeparture = HasAny(rest, DepartureStems)
            || (flight.Phase >= FlightPhase.Taxiing && HasAny(rest, ["partida"]) && !HasAny(rest, ["motor"]));
        var ready = HasAny(rest, RequestStems) || HasAny(rest, ReadyStems);

        return wantsDeparture && ready ? new RequestDeparture(callsign, FindRunwayInReadback(rest)) : null;
    }

    // What the pilot says in the circuit: asking to land, the legs, a go-around. Repeating an instruction (which has "reporte", "autorizado")
    // is none of these, and neither are the words of the readback of a clearance that is waiting for it.
    private static Intent? ArrivalIntent(List<string> rest, FlightContext flight, bool repeating, string? callsign)
    {
        if (repeating)
        {
            return null;
        }

        // "pista livre", "livrando a pista", "livrei a pista" (the instruction "após livrar a pista" is a readback and was left above).
        if (HasAny(rest, ["livr"]) && rest.Contains("pista"))
        {
            return new ReportRunwayVacated(callsign);
        }

        if (HasAny(rest, GoAroundStems))
        {
            return new ReportGoingAround(callsign);
        }

        if (rest.Contains("downwind") || (rest.Contains("perna") && rest.Contains("vento")))
        {
            return new ReportDownwind(callsign);
        }

        // "pernabase": the recognizer glues the two words.
        if (rest.Contains("base") || rest.Contains("pernabase"))
        {
            return new ReportBaseLeg(callsign);
        }

        if (rest.Contains("final"))
        {
            return new ReportFinal(callsign);
        }

        if (!HasAny(rest, LandingStems))
        {
            return null;
        }

        // "pouso pista uno cinco, PRABC" after "pista uno cinco, pouso autorizado" is the pilot reading it back, wrongly perhaps.
        var answering = flight.PendingReadback is CircuitClearance or LandingClearance && !HasAny(rest, RequestStems);

        return answering ? null : new RequestLanding(callsign);
    }

    // "no ponto de espera", "em ponto de espera", "holding": the aircraft is there. The "no"/"em" before "ponto" is what tells
    // the report from "para o ponto de espera", the way a readback repeats the instruction.
    private static bool IsHoldingShortReport(List<string> tokens)
    {
        if (tokens.Contains("holding"))
        {
            return true;
        }

        for (var i = 1; i < tokens.Count - 1; i++)
        {
            if (tokens[i] == "ponto" && tokens[i - 1] is "no" or "em" or "na" && (tokens[i + 1] == "espera" || (tokens[i + 1] == "de" && i + 2 < tokens.Count && tokens[i + 2] == "espera")))
            {
                return true;
            }
        }

        return false;
    }

    // Without a request word, a pilot who names what the controller just instructed is answering it; one who names
    // something else ("taxi" while the startup waits) is asking for it.
    private static bool Answers(AtcInstruction? pending, bool pushback, bool startup, bool taxi) => pending switch
    {
        TaxiClearance => taxi,
        StartupApproved => startup,
        PushbackApproved => pushback || startup,
        TaxiToApron => taxi,
        _ => false,
    };

    /// <summary>True when the words mention starting the engines ("acionamento", "partida").</summary>
    internal static bool MentionsStartup(IReadOnlyList<string> tokens) => HasAny(tokens, StartupStems);

    /// <summary>True when the words mention lining up ("alinhe e mantenha", "alinhando").</summary>
    internal static bool MentionsLineUp(IReadOnlyList<string> tokens) => HasAny(tokens, LineUpStems);

    /// <summary>True when the words mention the takeoff ("autorizado decolagem", "decolar").</summary>
    internal static bool MentionsTakeoff(IReadOnlyList<string> tokens) => HasAny(tokens, DepartureStems);

    /// <summary>True when the words mention the circuit ("autorizado circuito de tráfego").</summary>
    internal static bool MentionsCircuit(IReadOnlyList<string> tokens) => HasAny(tokens, ["circuito"]);

    /// <summary>True when the words mention landing ("pouso autorizado", "autorizado pousar").</summary>
    internal static bool MentionsLanding(IReadOnlyList<string> tokens) => HasAny(tokens, ["pous"]);

    /// <summary>True when the words mention the apron ("taxie para o pátio").</summary>
    internal static bool MentionsApron(IReadOnlyList<string> tokens) => HasAny(tokens, ["patio"]);

    /// <summary>True when the words mention pushback.</summary>
    internal static bool MentionsPushback(IReadOnlyList<string> tokens) => HasAny(tokens, PushbackStems);

    /// <summary>The runway the pilot named after the word "pista" ("pista dois três esquerda" is "23L"), or null.</summary>
    internal static string? FindRunway(IReadOnlyList<string> tokens) => FindRunwayAfter(tokens, token => token == "pista");

    /// <summary>
    /// The runway in a readback: after "pista" if the pilot said it, otherwise the first number that is a runway
    /// ("para o ponto de espera da três três"). Only used on a readback, where the controller just said which runway,
    /// because in a request a number may be something else ("box 3").
    /// </summary>
    internal static string? FindRunwayInReadback(IReadOnlyList<string> tokens) =>
        FindRunway(tokens) ?? FindRunwayAfter(tokens, _ => true, includeStart: true);

    private static string? FindRunwayAfter(IReadOnlyList<string> tokens, Func<string, bool> marker, bool includeStart = false)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!marker(tokens[i]))
            {
                continue;
            }

            var start = includeStart ? i : i + 1;
            var run = RunwayFromDecimal(SpokenCode.ReadRun(tokens, start, out var end));
            var digits = new string([.. run.TakeWhile(char.IsAsciiDigit)]);

            if (digits.Length == 0)
            {
                continue;
            }

            var side = SideAt(tokens, end, run, digits);

            if (RunwayDesignator.TryParse(digits + side, out _, out _))
            {
                return RunwayDesignator.Normalize(digits + side);
            }
        }

        return null;
    }

    private static string SideAt(IReadOnlyList<string> tokens, int end, string run, string digits)
    {
        // "29 l" read as a run is "29L": the letter came with the number.
        if (run.Length == digits.Length + 1 && run[^1] is 'L' or 'R' or 'C')
        {
            return run[^1].ToString();
        }

        if (end >= tokens.Count)
        {
            return string.Empty;
        }

        return tokens[end] switch
        {
            "esquerda" or "esquerdo" or "left" => "L",
            "direita" or "direito" or "right" => "R",
            "centro" or "central" or "center" => "C",
            _ => string.Empty,
        };
    }

    // "box 3", "posição 14", "pátio 1": the word and what follows it, kept as the pilot said it.
    private static string? FindStand(List<string> tokens)
    {
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            if (!StandWords.Contains(tokens[i], StringComparer.Ordinal))
            {
                continue;
            }

            var run = SpokenCode.ReadRun(tokens, i + 1, out _);

            if (run.Length > 0)
            {
                return $"{tokens[i]} {run}";
            }
        }

        return null;
    }

    // Finds the longest stretch of words that is this aircraft's callsign and takes it out of the sentence, so its
    // digits and letters are not mistaken for a runway or a position.
    private static List<string> RemoveCallsign(IReadOnlyList<string> tokens, string callsign, out bool found)
    {
        found = false;

        // The exact callsign, or its abbreviation, wherever it is; and only if there is none, the callsign with a letter wrong, missing or
        // extra, which is the recognizer's slip and not another aircraft.
        foreach (var matches in new Func<string, string, bool>[] { SpokenText.IsSameCallsign, SpokenText.IsCloseCallsign })
        {
            for (var start = 0; start < tokens.Count; start++)
            {
                for (var length = Math.Min(LongestCallsignInTokens, tokens.Count - start); length >= 1; length--)
                {
                    var candidate = string.Join(' ', tokens.Skip(start).Take(length));

                    if (matches(candidate, callsign))
                    {
                        found = true;
                        return [.. tokens.Take(start), .. tokens.Skip(start + length)];
                    }
                }
            }
        }

        return [.. tokens];
    }

    // Letters alone do not count: "a", "o" and "e" are Portuguese words before they are letters of the alphabet.
    private static bool HasNumber(List<string> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var index = i;

            if (SpokenCode.TryRead(tokens, ref index, out var value) && value.Any(char.IsAsciiDigit))
            {
                return true;
            }
        }

        return false;
    }

    // "1,5" and "3,3 L": the recognizer writes a runway read digit by digit as a decimal number. After "pista" or in a
    // readback, a single digit, a decimal separator and more digits is the runway ("15", "33L"), never a frequency.
    private static string RunwayFromDecimal(string run) =>
        run.Length >= 3 && run[1] == '.' && char.IsAsciiDigit(run[0]) && char.IsAsciiDigit(run[2]) ? run.Remove(1, 1) : run;

    // A stem of five letters or more is also looked for inside a word, because the recognizer glues words together when
    // the pilot speaks fast ("solicituacionamento"). Shorter stems ("tax", "push") would match too many other words.
    private static bool HasAny(IReadOnlyList<string> tokens, params string[][] stemLists) =>
        tokens.Any(t => stemLists.Any(stems => stems.Any(stem =>
            t.StartsWith(stem, StringComparison.Ordinal) || (stem.Length >= GluedStemLength && t.Contains(stem, StringComparison.Ordinal)))));
}
