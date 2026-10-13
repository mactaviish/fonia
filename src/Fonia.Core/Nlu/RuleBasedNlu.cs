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

    private static readonly string[] SayAgainWords =
        ["repita", "repete", "repetir", "repeti", "novamente", "denovo", "copiei", "entendi", "confirme", "como"];

    // The pilot is asking for something.
    private static readonly string[] RequestStems =
        ["solicit", "pedi", "pedido", "gostaria", "gostariamos", "preciso", "posso", "quero", "instruco", "instrucao", "pronto", "prontos"];

    // The pilot is repeating what the controller said, or acknowledging it.
    private static readonly string[] ReadbackStems =
        ["autorizad", "aprovad", "ciente", "copiad", "coteja", "chamo", "chamarei", "chamara", "reporto", "reportarei", "aguardo", "aguardando", "mantenho", "afirmo", "negativo"];

    private static readonly string[] PushbackStems = ["pushback", "push", "empurr"];
    private static readonly string[] StartupStems = ["acion", "partida", "start"];
    private static readonly string[] TaxiStems = ["tax"];
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
        var taxi = HasAny(rest, TaxiStems);
        var asking = HasAny(rest, RequestStems) && !HasAny(rest, ["chame", "chamo", "chamarei"]);
        var repeating = HasAny(rest, ReadbackStems);

        if (!pushback && !startup && !taxi)
        {
            // Said something, but no request: with an instruction waiting, it is the pilot answering it.
            return flight.PendingReadback is not null && rest.Count > 0
                ? new Readback(callsign, string.Join(' ', rest))
                : new Unknown(text ?? string.Empty);
        }

        if (repeating && !asking || (flight.PendingReadback is not null && !asking))
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

    /// <summary>The runway the pilot named ("pista dois três esquerda" is "23L"), or null.</summary>
    internal static string? FindRunway(IReadOnlyList<string> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] != "pista")
            {
                continue;
            }

            var run = SpokenCode.ReadRun(tokens, i + 1, out var end);
            var digits = new string([.. run.TakeWhile(char.IsAsciiDigit)]);
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

        for (var start = 0; start < tokens.Count; start++)
        {
            for (var length = Math.Min(LongestCallsignInTokens, tokens.Count - start); length >= 1; length--)
            {
                var candidate = string.Join(' ', tokens.Skip(start).Take(length));

                if (SpokenText.IsSameCallsign(candidate, callsign))
                {
                    found = true;
                    return [.. tokens.Take(start), .. tokens.Skip(start + length)];
                }
            }
        }

        return [.. tokens];
    }

    private static bool HasAny(IReadOnlyList<string> tokens, params string[][] stemLists) =>
        tokens.Any(t => stemLists.Any(stems => stems.Any(stem => t.StartsWith(stem, StringComparison.Ordinal))));
}
