using System.Globalization;
using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;

namespace Fonia.App;

/// <summary>
/// ATC by text: the user types what the pilot says and the controller of the chosen airport answers in text. It joins
/// the pieces of the pipeline that follow the speech-to-text (NLU, engine, phraseology); everything comes in through
/// the constructor, so tests drive it with a string as the keyboard and no simulator.
/// </summary>
/// <param name="callsignFromSim">Reads the aircraft's registration from the simulator; used only when no callsign was given.</param>
/// <param name="interactive">True on a terminal: shows the "piloto&gt;" prompt.</param>
/// <param name="voiceMode">When given, the pilot speaks to the microphone and the controller answers aloud, instead of typing.</param>
internal sealed class AtcConsole(
    IAirportCatalog catalog,
    INlu nlu,
    IAtcEngine engine,
    IPhraseology phraseology,
    TextReader input,
    TextWriter output,
    TextWriter error,
    Func<AppOptions, CancellationToken, Task<string?>> callsignFromSim,
    bool interactive,
    IVoiceMode? voiceMode = null)
{
    private static readonly string[] ExitWords = ["sair", "exit", "quit"];

    /// <summary>Returns the exit code: 0 normal, 1 failure while running, 2 wrong usage.</summary>
    public async Task<int> RunAsync(AppOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!catalog.TryGet(options.AtcAirport, out var airport))
        {
            error.WriteLine($"fonia: aeroporto '{options.AtcAirport}' não encontrado nos dados (use o código ICAO, ex.: SBCT).");
            return 2;
        }

        AirportContext context;

        try
        {
            context = AirportContext.Create(airport, options.Runway);
        }
        catch (ArgumentException)
        {
            var open = string.Join(", ", airport.Runways.Where(r => !r.Closed).Select(r => r.Name));

            error.WriteLine($"fonia: {airport.Ident} não tem a pista '{options.Runway}' aberta. Pistas: {(open.Length > 0 ? open : "nenhuma nos dados")}.");
            return 2;
        }

        string? callsign;

        try
        {
            callsign = Normalize(options.Callsign ?? await callsignFromSim(options, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 0;
        }

        if (callsign is null)
        {
            error.WriteLine("fonia: não consegui o indicativo da aeronave no simulador. Use --callsign <indicativo> (ex.: --callsign PR-ABC).");
            return 1;
        }

        WriteHeader(context, callsign);

        var flight = new FlightContext(callsign, FlightPhase.Parked, airport.Ident, PendingReadback: null);

        if (voiceMode is not null)
        {
            return await voiceMode.RunAsync(context, flight, ct);
        }

        var phrase = new PhraseContext(callsign, context);

        while (true)
        {
            if (interactive)
            {
                await output.WriteAsync("piloto> ");
            }

            string? line;

            try
            {
                line = await input.ReadLineAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }

            if (line is null || ExitWords.Contains(line.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var decision = engine.Handle(nlu.Interpret(line, flight), flight, context);
            flight = decision.NextContext;

            Show(phraseology.Speak(decision.Instruction, phrase), context);
        }

        return 0;
    }

    // "PR-ABC" and "pr abc" are the same aircraft: the engine and the NLU work with letters and digits in capitals.
    private static string? Normalize(string? callsign)
    {
        var letters = new string([.. (callsign ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

        return letters.Length == 0 ? null : letters;
    }

    private void WriteHeader(AirportContext context, string callsign)
    {
        var airport = context.Airport;

        output.WriteLine($"Fonia ATC - {airport.Ident} {airport.Name}. Aeronave {callsign}.");
        output.WriteLine(context.Unit == ServiceUnit.None
            ? "Sem serviço de controle neste aeródromo: ninguém responde."
            : $"Responde: {phraseology.UnitName(context)}. Pista em uso: {context.RunwayInUse ?? "nenhuma nos dados"}.");

        var frequencies = new[] { AirportFrequencyType.Ground, AirportFrequencyType.Tower, AirportFrequencyType.Afis }
            .Select(type => airport.FrequencyOf(type) is { } f ? $"{type} {f.Frequency.MHz.ToString("0.0##", CultureInfo.InvariantCulture)}" : null)
            .OfType<string>()
            .ToList();

        if (frequencies.Count > 0)
        {
            output.WriteLine($"Frequências: {string.Join(", ", frequencies)}.");
        }

        if (voiceMode is null)
        {
            output.WriteLine("Digite a fala do piloto (\"sair\" encerra).");
        }
    }

    private void Show(Utterance? utterance, AirportContext context)
    {
        switch (utterance)
        {
            case null:
                output.WriteLine("(o controlador não responde)");
                break;
            case { Kind: UtteranceKind.Notice }:
                output.WriteLine($"[aviso] {utterance.Text}");
                break;
            default:
                output.WriteLine($"{phraseology.UnitName(context)}: {utterance.Text}");
                break;
        }
    }
}
