using System.Globalization;
using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Pipeline;
using Fonia.Core.Sim;
using Fonia.Core.Weather;

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
    Func<Frequency?>? liveCom1 = null,
    TimeProvider? timeProvider = null,
    IMetarSource? metarSource = null,
    Func<AirportContext, string, CaseLog?>? cases = null,
    Func<double, double, (Airport Airport, double Kilometers)?>? nearestAirport = null,
    Func<CancellationToken, IAsyncEnumerable<AircraftState>?>? states = null,
    Func<AircraftState?>? liveAircraft = null)
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

        var weather = options.NoMetar ? null : await ReadWeatherAsync(airport.Ident, ct);

        if (weather is not null)
        {
            context = context.WithWeather(weather.Metar, keepRunway: options.Runway is not null);
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

        WriteHeader(context, callsign, options.NoMetar ? null : weather, metarSource is not null && !options.NoMetar);

        var flight = new FlightContext(callsign, FlightPhase.Parked, airport.Ident, PendingReadback: null);

        // The simulator may load another flight while the app runs: the airport, the aircraft and the flight start over, with no one asking.
        var currentContext = context;
        var currentCallsign = callsign;

        async Task<FlightRestart?> RestartFlight(AircraftState state, CancellationToken token)
        {
            var restart = await RestartAsync(state, currentContext, currentCallsign, options, token);

            if (restart is not null)
            {
                currentContext = restart.Airport;
                currentCallsign = restart.Flight.Callsign;
            }

            return restart;
        }

        if (voiceMode is not null)
        {
            return await voiceMode.RunAsync(context, flight, ct, nearestAirport is null ? null : RestartFlight);
        }

        var log = cases?.Invoke(context, callsign);
        FlightRestart? pending = null;
        using var stopFollowing = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var following = nearestAirport is not null && states?.Invoke(stopFollowing.Token) is { } stream
            ? FollowAsync(stream, RestartFlight, restart => Volatile.Write(ref pending, restart), stopFollowing.Token)
            : Task.CompletedTask;

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

            if (Interlocked.Exchange(ref pending, null) is { } restarted)
            {
                context = restarted.Airport;
                flight = restarted.Flight;
                callsign = restarted.Flight.Callsign;
                manualCom1 = options.Com1;
                log?.StartFlight(context, callsign);
            }

            if (IsMarkCommand(line))
            {
                output.WriteLine(log is null
                    ? "Os casos não estão sendo guardados (--no-cases)."
                    : log.Mark() ? "Troca marcada como errada: guardada para melhoria. Obrigado!" : "Nada para marcar ainda (ou já foi marcada).");
                continue;
            }

            if (IsEventCommand(line))
            {
                var radio = FrequencyResolver.Apply(context, manualCom1 ?? liveCom1?.Invoke());
                flight = HandleEventCommand(line, flight, radio);
                continue;
            }

            // Who answers is who is on the frequency the radio is on at this moment.
            var onTheFrequency = FrequencyResolver.Apply(context, manualCom1 ?? liveCom1?.Invoke());
            // What the simulator shows now goes with the flight, so that a readback is judged by the situation.
            flight = liveAircraft?.Invoke() is { } shown ? flight with { Aircraft = shown } : flight;

            var before = flight;
            var intent = nlu.Interpret(line, flight);
            var decision = engine.Handle(intent, flight, onTheFrequency);
            flight = decision.NextContext;

            var reply = phraseology.Speak(decision.Instruction, new PhraseContext(callsign, onTheFrequency, (timeProvider ?? TimeProvider.System).GetUtcNow()));
            Show(reply, onTheFrequency);

            if (log?.Record(new ExchangeResult(line, intent, decision, reply, null, new StageTimings(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero), phraseology.UnitName(onTheFrequency), before)) is { Count: > 0 } reasons)
            {
                output.WriteLine($"  (guardado para melhoria: {string.Join(", ", reasons)})");
            }
        }

        await stopFollowing.CancelAsync();
        await following;
        return 0;
    }

    // Reads the states of the simulator and, when the flight in them is another one, builds the new start and hands it over.
    private static async Task FollowAsync(
        IAsyncEnumerable<AircraftState> stream,
        Func<AircraftState, CancellationToken, Task<FlightRestart?>> restartFlight,
        Action<FlightRestart> hand,
        CancellationToken ct)
    {
        var changes = new FlightChangeDetector();

        try
        {
            await foreach (var state in stream.WithCancellation(ct))
            {
                if (changes.Observe(state) is { } change && await restartFlight(change.State, ct) is { } restart)
                {
                    hand(restart);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The console is closing.
        }
    }

    // The simulator loaded another flight: the airport nearest to the aircraft (if there is one near), its weather and the aircraft's
    // callsign make the new start; the state of the flight goes back to the apron. Returns null when the app cannot tell where it is.
    private async Task<FlightRestart?> RestartAsync(AircraftState state, AirportContext current, string currentCallsign, AppOptions options, CancellationToken ct)
    {
        if (nearestAirport is null)
        {
            return null;
        }

        var callsign = Normalize(state.Registration) ?? currentCallsign;
        var target = current.Airport;
        var near = nearestAirport(state.LatitudeDegrees, state.LongitudeDegrees);

        if (near is { } found && found.Kilometers <= StartupWizard.MostKilometersFromAirport)
        {
            target = found.Airport;
        }
        else
        {
            output.WriteLine($"Novo voo detectado, mas nenhum aeroporto por perto: o Fonia continua em {target.Ident}.");
        }

        var sameAirport = string.Equals(target.Ident, current.Airport.Ident, StringComparison.OrdinalIgnoreCase);
        AirportContext context;

        try
        {
            context = AirportContext.Create(target, sameAirport ? options.Runway : null);
        }
        catch (ArgumentException)
        {
            context = AirportContext.Create(target);
        }

        var weather = options.NoMetar ? null : await ReadWeatherAsync(target.Ident, ct);

        if (weather is not null)
        {
            context = context.WithWeather(weather.Metar, keepRunway: sameAirport && options.Runway is not null);
        }

        output.WriteLine();
        output.WriteLine($"Novo voo detectado no simulador: aeronave {callsign}, {target.Ident}.");
        WriteHeader(context, callsign, weather, metarSource is not null && !options.NoMetar);

        return new FlightRestart(context, new FlightContext(callsign, FlightPhase.Parked, target.Ident, PendingReadback: null));
    }

    // "PR-ABC" and "pr abc" are the same aircraft: the engine and the NLU work with letters and digits in capitals.
    private static string? Normalize(string? callsign)
    {
        var letters = new string([.. (callsign ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

        return letters.Length == 0 ? null : letters;
    }

    // The METAR is an extra: the radio works without it, so it waits a few seconds at most and any failure is just "no METAR".
    private async Task<MetarReading?> ReadWeatherAsync(string icao, CancellationToken ct)
    {
        if (metarSource is null)
        {
            return null;
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            return await metarSource.GetAsync(icao, limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private void WriteHeader(AirportContext context, string callsign, MetarReading? weather, bool weatherWasAsked)
    {
        var airport = context.Airport;

        output.WriteLine($"Fonia ATC - {airport.Ident} {airport.Name}. Aeronave {callsign}.");
        output.WriteLine(context.Unit == ServiceUnit.None
            ? "Sem serviço de controle neste aeródromo: ninguém responde."
            : $"Responde: {phraseology.UnitName(context)}. Pista em uso: {context.RunwayInUse ?? "nenhuma nos dados"}.");

        if (weather is not null)
        {
            output.WriteLine(DescribeWeather(weather));
        }
        else if (weatherWasAsked)
        {
            output.WriteLine("Sem METAR (sem rede ou sem dados): o vento e o ajuste de altímetro ficam de fora das falas.");
        }

        var frequencies = new[] { AirportFrequencyType.Ground, AirportFrequencyType.Tower, AirportFrequencyType.Afis, AirportFrequencyType.Atis }
            .Select(type => airport.FrequencyOf(type) is { } f ? $"{type} {f.Frequency.MHz.ToString("0.0##", CultureInfo.InvariantCulture)}" : null)
            .OfType<string>()
            .ToList();

        if (frequencies.Count > 0)
        {
            output.WriteLine($"Frequências: {string.Join(", ", frequencies)}.");
        }

        if (voiceMode is null)
        {
            output.WriteLine("Digite a fala do piloto (\"sair\" encerra; \"/com1 118.55\" muda a frequência do rádio; \"/evento decolou\" simula a decolagem; \"/evento pousou\", o pouso; \"/errado\" marca a última troca como mal entendida).");
        }
    }

    // "METAR SBCT 152000Z 23003KT ... (observado às 20:00Z); pista em uso pelo vento: 15."
    private string DescribeWeather(MetarReading weather)
    {
        var age = (timeProvider ?? TimeProvider.System).GetUtcNow() - weather.ObservedAt;
        var when = $"observado às {weather.ObservedAt.UtcDateTime:HH:mm}Z, há {FormatAge(age)}{(weather.Stale ? ", guardado: sem rede" : string.Empty)}";

        return $"{weather.Metar.Raw.Replace("METAR ", string.Empty, StringComparison.Ordinal)} ({when}).";
    }

    private static string FormatAge(TimeSpan age) => age < TimeSpan.FromMinutes(1)
        ? "instantes"
        : age < TimeSpan.FromHours(1) ? $"{(int)age.TotalMinutes} min" : $"{(int)age.TotalHours} h {age.Minutes} min";

    private static bool IsRadioCommand(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith("/com1", StringComparison.OrdinalIgnoreCase) && (trimmed.Length == 5 || char.IsWhiteSpace(trimmed[5]));
    }

    private static bool IsMarkCommand(string line) => line.Trim().Equals("/errado", StringComparison.OrdinalIgnoreCase);

    private static bool IsEventCommand(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith("/evento", StringComparison.OrdinalIgnoreCase) && (trimmed.Length == 7 || char.IsWhiteSpace(trimmed[7]));
    }

    // "/evento decolou" and "/evento pousou" stand for what the telemetry shows with a simulator: the aircraft left, or touched, the ground, now.
    private FlightContext HandleEventCommand(string line, FlightContext flight, AirportContext onTheFrequency)
    {
        var argument = line.Trim()[7..].Trim();

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();

        FlightEvent? flightEvent = argument.ToUpperInvariant() switch
        {
            "DECOLOU" => new TookOff(now),
            "POUSOU" => new TouchedDown(now),
            _ => null,
        };

        if (flightEvent is null)
        {
            error.WriteLine("fonia: evento desconhecido. Use /evento decolou ou /evento pousou.");
            return flight;
        }

        var decision = engine.Handle(flightEvent, flight, onTheFrequency);

        if (phraseology.Speak(decision.Instruction, new PhraseContext(flight.Callsign, onTheFrequency, now)) is { } utterance)
        {
            Show(utterance, onTheFrequency);
        }
        else
        {
            output.WriteLine("(o controlador não diz nada sobre isso)");
        }

        return decision.NextContext;
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
