using Fonia.Core.Atc;
using Fonia.Core.Audio;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Pipeline;
using Fonia.Core.Sim;
using Fonia.Core.Speech;

namespace Fonia.App;

/// <summary>How the ATC console talks to the pilot by voice instead of by text.</summary>
internal interface IVoiceMode
{
    /// <returns>The exit code: 0 normal, 1 when the voice could not be started.</returns>
    /// <param name="onNewFlight">Called with the first state of another flight the simulator loads while the radio is on; it builds the new start.</param>
    Task<int> RunAsync(AirportContext airport, FlightContext flight, CancellationToken ct, Func<AircraftState, CancellationToken, Task<FlightRestart?>>? onNewFlight = null);
}

/// <summary>
/// The voice mode of <c>--atc --voice</c>: loads the models ahead of time, so that the first transmission is not the one
/// that pays for it, then runs the radio until the user stops it. Everything comes in through the constructor, so tests
/// drive it with a fake microphone, key and loudspeaker and no model.
/// </summary>
internal sealed class VoiceMode(
    IAudioCapture capture,
    IPushToTalk pushToTalk,
    ITranscriber transcriber,
    ISynthesizer synthesizer,
    IAudioPlayer player,
    INlu nlu,
    IAtcEngine engine,
    IPhraseology phraseology,
    VoiceSelector voices,
    RadioIntensity radio,
    string keyName,
    TextWriter output,
    TextWriter error,
    Random random,
    Func<Frequency?>? com1 = null,
    Func<CancellationToken, IAsyncEnumerable<AircraftState>?>? states = null,
    Func<AirportContext, string, CaseLog?>? cases = null,
    IPushToTalk? markKey = null,
    string markKeyName = "",
    Func<AircraftState?>? aircraft = null) : IVoiceMode
{
    public async Task<int> RunAsync(AirportContext airport, FlightContext flight, CancellationToken ct, Func<AircraftState, CancellationToken, Task<FlightRestart?>>? onNewFlight = null)
    {
        ArgumentNullException.ThrowIfNull(airport);
        ArgumentNullException.ThrowIfNull(flight);

        var station = phraseology.UnitName(airport);

        output.WriteLine("Carregando os modelos de voz (alguns segundos)...");

        try
        {
            var context = new TranscriptionContext(flight.Callsign, station);

            await Task.WhenAll(WarmUp(transcriber, context, ct), WarmUp(synthesizer, context, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 0;
        }
        catch (FileNotFoundException ex)
        {
            error.WriteLine($"fonia: {ex.Message}");
            return 1;
        }
#pragma warning disable CA1031 // Loading a model can fail in many ways (a native library, a corrupt file); all end the same way.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            error.WriteLine($"fonia: não foi possível carregar os modelos de voz: {ex.Message}");
            return 1;
        }

        var exchange = new VoiceExchange(transcriber, nlu, engine, phraseology, synthesizer, voices, radio, random);
        // What the controller could not follow is kept on the user's disk (and a key marks what it got wrong without knowing).
        var log = cases?.Invoke(airport, flight.Callsign);
        IVoiceLoopObserver observer = new ConsoleVoiceObserver(output, error, station);

        if (log is not null)
        {
            observer = new CaseRecordingObserver(observer, log, output);
        }

        var loop = new VoiceLoop(new SpeechRecorder(capture, pushToTalk), exchange, player, airport, flight, observer, com1, aircraft);

        // On the ATIS frequency of the airport the broadcast plays by itself, for as long as the radio stays there.
        var broadcast = new AtisBroadcast(
            synthesizer,
            radio,
            random,
            player,
            () => loop.Airport,
            () => com1?.Invoke(),
            (portuguese, english) =>
            {
                output.WriteLine($"  ATIS: {portuguese}");
                output.WriteLine($"  ATIS (en): {english}");
            },
            ex => error.WriteLine($"fonia: falha no ATIS: {ex.Message}"));

        // What the telemetry shows (the takeoff) is something the controller may say without being asked; read only once the radio is up.
        var following = states?.Invoke(ct) is { } stream ? FollowAsync(loop, stream, onNewFlight, log, ct) : Task.CompletedTask;

        var marking = log is not null && markKey is not null;

        if (marking)
        {
            markKey!.Pressed += OnMark;
            markKey.StartWatching();
        }

        var broadcasting = broadcast.RunAsync(ct);

        try
        {
            // Said only once the microphone and the key are open: whoever reads it may press the key at once.
            await loop.RunAsync(
                ct,
                () => output.WriteLine($"Pronto. Segure {keyName}, fale e solte: {station} responde em voz alta.{(marking ? $" Se ele entender errado, aperte {markKeyName} para marcar a troca." : string.Empty)} Ctrl+C encerra."));
            await following;
            await broadcasting;
        }
        finally
        {
            if (marking)
            {
                markKey!.Pressed -= OnMark;
                markKey.StopWatching();
            }
        }

        return 0;

        void OnMark(object? sender, EventArgs e) =>
            output.WriteLine(log!.Mark() ? "  Troca marcada como errada: guardada para melhoria. Obrigado!" : "  Nada para marcar ainda (ou já foi marcada).");
    }

    private static async Task FollowAsync(
        VoiceLoop loop,
        IAsyncEnumerable<AircraftState> states,
        Func<AircraftState, CancellationToken, Task<FlightRestart?>>? onNewFlight,
        CaseLog? log,
        CancellationToken ct)
    {
        var events = new FlightEventDetector();
        var changes = new FlightChangeDetector();
        var announcements = new List<Task>();

        try
        {
            await foreach (var state in states.WithCancellation(ct))
            {
                // Another flight: the controller, the flight and the events start over with what the simulator shows now.
                if (onNewFlight is not null && changes.Observe(state) is { } change && await onNewFlight(change.State, ct) is { } restart)
                {
                    loop.StartFlight(restart.Airport, restart.Flight);
                    log?.StartFlight(restart.Airport, restart.Flight.Callsign);
                    events = new FlightEventDetector();
                    events.Observe(change.State);
                    continue;
                }

                if (events.Observe(state) is { } flightEvent)
                {
                    // Not awaited: the controller waits for the channel by itself, and the stream must keep being read.
                    announcements.Add(loop.AnnounceAsync(flightEvent, ct));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The radio is stopping.
        }

        await Task.WhenAll(announcements);
    }

    private static Task WarmUp(object model, TranscriptionContext context, CancellationToken ct) =>
        model is IWarmUp warmable ? warmable.WarmUpAsync(context, ct) : Task.CompletedTask;
}
