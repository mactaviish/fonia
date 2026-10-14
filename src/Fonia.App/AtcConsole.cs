using System.Globalization;
using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;

namespace Fonia.App;

/// <summary>
/// ATC by text: the user types what the pilot says and the controller of the chosen airport answers in text. It joins
/// the pieces of the pipeline that follow the speech-to-text (NLU, engine, phraseology); everything comes in through
/// the constructor, so tests drive it with a string as the keyboard and no simulator.
/// </summary>
/// <param name="callsignFromSim">Reads the aircraft's registration from the simulator; used only when no callsign was given.</param>
/// <param name="interactive">True on a terminal: shows the "piloto&gt;" prompt.</param>
/// <param name="voiceMode">When given, the pilot speaks to the microphone and the controller answers aloud, instead of typing.</param>
/// <param name="liveCom1">The frequency COM1 is on, read from the simulator at every line; null without a simulator.</param>
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
    IVoiceMode? voiceMode = null,
    Func<Frequency?>? liveCom1 = null)
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

        // What the user set (--com1, or /com1 in the session) wins over what the simulator reports.
        var manualCom1 = options.Com1;

        var startingRadio = manualCom1 ?? liveCom1?.Invoke();

        if (startingRadio is not null)
        {
            output.WriteLine(DescribeRadio(FrequencyResolver.Apply(context, startingRadio)));
        }

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

            if (IsRadioCommand(line))
            {
                manualCom1 = HandleRadioCommand(line, manualCom1, liveCom1, context);
                continue;
            }

            // Who answers is who is on the frequency the radio is on at this moment.
            var onTheFrequency = FrequencyResolver.Apply(context, manualCom1 ?? liveCom1?.Invoke());
            var decision = engine.Handle(nlu.Interpret(line, flight), flight, onTheFrequency);
            flight = decision.NextContext;

            Show(phraseology.Speak(decision.Instruction, new PhraseContext(callsign, onTheFrequency)), onTheFrequency);
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
            output.WriteLine("Digite a fala do piloto (\"sair\" encerra; \"/com1 118.55\" muda a frequência do rádio).");
        }
    }

    private static bool IsRadioCommand(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith("/com1", StringComparison.OrdinalIgnoreCase) && (trimmed.Length == 5 || char.IsWhiteSpace(trimmed[5]));
    }

    // "/com1" shows the radio, "/com1 118.55" tunes it, "/com1 off" gives it back to what the airport has by default.
    private Frequency? HandleRadioCommand(string line, Frequency? manual, Func<Frequency?>? live, AirportContext context)
    {
        var argument = line.Trim()[5..].Trim();

        if (argument.Length == 0)
        {
            ShowRadio(manual ?? live?.Invoke(), context);
            return manual;
        }

        if (argument.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine("COM1 livre: responde a unidade do aeroporto (ou a frequência lida do simulador).");
            return null;
        }

        if (!TryParseFrequency(argument, out var tuned))
        {
            error.WriteLine($"fonia: frequência inválida '{argument}'. Use MHz entre 118 e 137, por exemplo /com1 118.55.");
            return manual;
        }

        ShowRadio(tuned, context);
        return tuned;
    }

    private void ShowRadio(Frequency? tuned, AirportContext context)
    {
        if (tuned is null)
        {
            output.WriteLine("COM1 sem leitura: responde a unidade do aeroporto.");
            return;
        }

        output.WriteLine(DescribeRadio(FrequencyResolver.Apply(context, tuned)));
    }

    // "COM1 119.5: TORRE Curitiba atende." or the notice that nobody does (with the frequencies the airport has).
    private string DescribeRadio(AirportContext onTheFrequency)
    {
        var frequency = onTheFrequency.Tuned!.Value;

        return onTheFrequency.Unit == ServiceUnit.None
            ? phraseology.Speak(new NoOneOnFrequency(frequency), new PhraseContext("X", onTheFrequency))!.Text
            : $"COM1 {frequency.MHz.ToString("0.0##", CultureInfo.InvariantCulture)}: {phraseology.UnitName(onTheFrequency)} atende.";
    }

    internal static bool TryParseFrequency(string text, out Frequency frequency)
    {
        frequency = default;

        if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var mhz) && mhz is >= 118 and <= 137)
        {
            frequency = Frequency.FromMhz(mhz);
            return true;
        }

        return false;
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
