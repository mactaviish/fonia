using Fonia.Core.Atc;
using Fonia.Core.Audio;
using Fonia.Core.Sim;

namespace Fonia.Core.Pipeline;

/// <summary>What the voice loop tells the interface as it goes. Called from arbitrary threads, one call at a time per exchange.</summary>
public interface IVoiceLoopObserver
{
    /// <summary>The text of what the pilot said, as soon as it is known.</summary>
    void OnHeard(string text);

    /// <summary>The exchange is done: what the controller answers (it is about to be played, if it has a voice).</summary>
    void OnReplied(ExchangeResult result);

    /// <summary>The controller spoke first, about something that happened to the flight.</summary>
    void OnAnnounced(AnnouncementResult result);

    /// <summary>The pilot pressed the key while the controller was talking or an answer was in the making: it was ignored.</summary>
    void OnChannelBusy();

    /// <summary>The key was held for too short a time to be speech.</summary>
    void OnTooShort(TimeSpan held);

    /// <summary>Something went wrong with one exchange (a model that is missing, a device that failed); the loop goes on.</summary>
    void OnFailed(Exception exception);
}

/// <summary>
/// The radio of the pilot: while it runs, whatever is said with the key held is answered by the controller, one exchange at
/// a time. Like a real frequency it is busy while the controller talks: a transmission made then is ignored. The state of
/// the flight is carried from one exchange to the next.
/// </summary>
public sealed class VoiceLoop(
    SpeechRecorder recorder,
    VoiceExchange exchange,
    IAudioPlayer player,
    AirportContext airport,
    FlightContext flight,
    IVoiceLoopObserver observer,
    Func<Frequency?>? com1 = null,
    Func<AircraftState?>? aircraft = null)
{
    private int _busy;
    private Task _current = Task.CompletedTask;
    private readonly Lock _flightGate = new();
    private AirportContext _airport = airport;
    private FlightContext _flight = flight;
    private int _generation;
    private CancellationToken _stop;

    /// <summary>
    /// The simulator loaded another flight: from the next transmission on, the controller is the one of <paramref name="newAirport"/> and the
    /// flight is <paramref name="newFlight"/>. What an exchange in the making was going to leave behind is dropped, so the old flight
    /// does not come back.
    /// </summary>
    public void StartFlight(AirportContext newAirport, FlightContext newFlight)
    {
        ArgumentNullException.ThrowIfNull(newAirport);
        ArgumentNullException.ThrowIfNull(newFlight);

        lock (_flightGate)
        {
            _generation++;
            _airport = newAirport;
            _flight = newFlight;
        }
    }

    private (int Generation, AirportContext Airport, FlightContext Flight) Snapshot()
    {
        lock (_flightGate)
        {
            // What the simulator shows of the aircraft now goes with the flight, so that a readback is judged by the situation.
            return (_generation, _airport, aircraft?.Invoke() is { } shown ? _flight with { Aircraft = shown } : _flight);
        }
    }

    // The flight after an exchange, unless another flight began meanwhile.
    private void Keep(int generation, FlightContext after)
    {
        lock (_flightGate)
        {
            if (generation == _generation)
            {
                _flight = after;
            }
        }
    }

    /// <summary>True while an exchange is being made or the answer is being played.</summary>
    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    /// <summary>Listens until <paramref name="ct"/> is cancelled, then cuts what is being said and closes the microphone and the key.</summary>
    /// <param name="onListening">Called once the microphone and the key are open, so that the user is told to speak only when it works.</param>
    public async Task RunAsync(CancellationToken ct, Action? onListening = null)
    {
        _stop = ct;
        recorder.SpeechRecorded += OnSpeech;
        recorder.SpeechDiscarded += OnDiscarded;

        try
        {
            recorder.Start();
            onListening?.Invoke();
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // Stopping is how the loop ends.
        }
        finally
        {
            recorder.SpeechRecorded -= OnSpeech;
            recorder.SpeechDiscarded -= OnDiscarded;
            recorder.Dispose();
            await _current;
        }
    }

    /// <summary>
    /// The controller speaks about an event of the flight. Like anyone on a frequency they wait for it to be free: if the pilot
    /// is being answered, or an answer is playing, this goes after it.
    /// </summary>
    public async Task AnnounceAsync(FlightEvent flightEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(flightEvent);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop);

        try
        {
            while (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), linked.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var (generation, airportNow, flightNow) = Snapshot();
            var onTheFrequency = FrequencyResolver.Apply(airportNow, com1?.Invoke());
            var result = await exchange.AnnounceAsync(flightEvent, flightNow, onTheFrequency, linked.Token);
            Keep(generation, result.Decision.NextContext);

            if (result.Reply is not null)
            {
                observer.OnAnnounced(result);
            }

            if (result.Speech is not null)
            {
                await player.PlayAsync(result.Speech, linked.Token);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // Stopping: what was being said is cut.
        }
#pragma warning disable CA1031 // An announcement can fail in many ways (models, devices); one failure must not end the radio.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            observer.OnFailed(ex);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private void OnDiscarded(object? sender, SpeechDiscardedEventArgs e) => observer.OnTooShort(e.Duration);

    private void OnSpeech(object? sender, RecordedSpeech speech)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            observer.OnChannelBusy();
            return;
        }

        _current = Task.Run(() => ExchangeAsync(speech));
    }

    private async Task ExchangeAsync(RecordedSpeech speech)
    {
        try
        {
            // Who answers is who is on the frequency the pilot has tuned right now, which may change between two transmissions.
            var (generation, airportNow, flightNow) = Snapshot();
            var onTheFrequency = FrequencyResolver.Apply(airportNow, com1?.Invoke());
            var result = await exchange.RunAsync(speech, flightNow, onTheFrequency, observer.OnHeard, _stop);
            Keep(generation, result.Decision.NextContext);
            observer.OnReplied(result);

            if (result.Speech is not null)
            {
                await player.PlayAsync(result.Speech, _stop);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // The program is stopping: whatever was being said is cut, and that is not a failure.
        }
#pragma warning disable CA1031 // An exchange can fail in many ways (models, devices); one failure must not end the radio.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            observer.OnFailed(ex);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }
}
