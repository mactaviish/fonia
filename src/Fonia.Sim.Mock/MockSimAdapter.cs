using Fonia.Core.Sim;

namespace Fonia.Sim.Mock;

/// <summary>
/// In-memory <see cref="ISimAdapter"/> whose state is programmed by the caller. It is the base of
/// every automated test: no simulator, no timing, no I/O. Thread-safe.
/// </summary>
/// <remarks>
/// <see cref="SetState"/> / <see cref="Update"/> play the role of the simulator producing a snapshot.
/// A state programmed before <see cref="ConnectAsync"/> is delivered as the first snapshot on connect,
/// and the latest programmed state is delivered again when a lost connection returns.
/// <see cref="PlayAsync"/> and <see cref="ReplayAsync(IAsyncEnumerable{AircraftState}, double, CancellationToken)"/>
/// play scripted or recorded sequences. <see cref="SimulateConnectionLoss"/> / <see cref="RestoreConnection"/> play the simulator going away
/// and coming back, as a real adapter that reconnects on its own would see it.
/// </remarks>
public sealed class MockSimAdapter : ISimAdapter, IComRadioControl
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly AircraftStateBroadcaster _broadcaster = new();
    private SimConnectionState _connectionState = SimConnectionState.Disconnected;
    private AircraftState? _programmedState;
    private bool _disposed;

    /// <param name="timeProvider">Clock used for the delays of <see cref="PlayAsync"/>; defaults to the system clock.</param>
    public MockSimAdapter(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<SimConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public SimConnectionState ConnectionState
    {
        get
        {
            lock (_gate)
            {
                return _connectionState;
            }
        }
    }

    public AircraftState? LatestState
    {
        get
        {
            lock (_gate)
            {
                return _broadcaster.Latest;
            }
        }
    }

    public Task ConnectAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        List<SimConnectionStateChangedEventArgs> changes = [];

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Already connected, or already reconnecting after a loss: the adapter owns the connection.
            if (_connectionState != SimConnectionState.Disconnected)
            {
                return Task.CompletedTask;
            }

            ChangeState(SimConnectionState.Connecting, changes);
            ChangeState(SimConnectionState.Connected, changes);
            DeliverProgrammedState();
        }

        Raise(changes);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        List<SimConnectionStateChangedEventArgs> changes = [];

        lock (_gate)
        {
            Disconnect(changes);
        }

        Raise(changes);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Plays the simulator going away: <c>Connected → Connecting</c>. Streams stay open and
    /// <see cref="LatestState"/> is kept; states programmed meanwhile wait for <see cref="RestoreConnection"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Not connected.</exception>
    public void SimulateConnectionLoss()
    {
        List<SimConnectionStateChangedEventArgs> changes = [];

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_connectionState != SimConnectionState.Connected)
            {
                throw new InvalidOperationException("A connection loss can only be simulated while connected.");
            }

            ChangeState(SimConnectionState.Connecting, changes);
        }

        Raise(changes);
    }

    /// <summary>
    /// Plays the simulator coming back: <c>Connecting → Connected</c>, delivering the latest programmed
    /// state to every stream.
    /// </summary>
    /// <exception cref="InvalidOperationException">No connection loss in progress.</exception>
    public void RestoreConnection()
    {
        List<SimConnectionStateChangedEventArgs> changes = [];

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_connectionState != SimConnectionState.Connecting)
            {
                throw new InvalidOperationException("There is no connection loss to restore.");
            }

            ChangeState(SimConnectionState.Connected, changes);
            DeliverProgrammedState();
        }

        Raise(changes);
    }

    public IAsyncEnumerable<AircraftState> StreamAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // The subscription starts at this call, so no snapshot published afterwards is missed.
            return _broadcaster.Subscribe(ct);
        }
    }

    public ValueTask DisposeAsync()
    {
        List<SimConnectionStateChangedEventArgs> changes = [];

        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            Disconnect(changes);
        }

        Raise(changes);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Programs a new snapshot. While connected it becomes <see cref="LatestState"/> and is delivered
    /// to every stream; while disconnected or reconnecting it is kept and delivered when the connection (re)appears.
    /// </summary>
    public void SetState(AircraftState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _programmedState = state;

            if (_connectionState == SimConnectionState.Connected)
            {
                Publish(state);
            }
        }
    }

    /// <summary>
    /// Programs a new snapshot derived from the most recently programmed one.
    /// </summary>
    /// <exception cref="InvalidOperationException">No state has been programmed yet.</exception>
    public void Update(Func<AircraftState, AircraftState> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (_gate)
        {
            // The programmed state is always the newest: it equals LatestState while connected and is
            // ahead of it while disconnected or reconnecting.
            var current = _programmedState
                ?? throw new InvalidOperationException("No state to update: call SetState first.");

            SetState(change(current));
        }
    }

    /// <summary>
    /// Plays a scripted sequence of snapshots through <see cref="SetState"/>, waiting <paramref name="interval"/>
    /// between consecutive ones (not after the last). Completes once the last snapshot has been published.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled; snapshots already published stay published.</exception>
    public async Task PlayAsync(IEnumerable<AircraftState> states, TimeSpan interval, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero);

        var first = true;

        foreach (var state in states)
        {
            ct.ThrowIfCancellationRequested();

            if (!first && interval > TimeSpan.Zero)
            {
                await Task.Delay(interval, _timeProvider, ct).ConfigureAwait(false);
            }

            first = false;
            SetState(state);
        }
    }

    /// <summary>
    /// Replays a recording at its original pace: the first snapshot goes out immediately and each next one
    /// after the gap between their <see cref="AircraftState.Timestamp"/> values divided by
    /// <paramref name="speed"/> (1 = real time, 2 = twice as fast, <see cref="double.PositiveInfinity"/> = no waiting).
    /// Equal or backwards timestamps are published without waiting. Completes after the last snapshot.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="speed"/> is not a positive number.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled; snapshots already published stay published.</exception>
    public async Task ReplayAsync(IAsyncEnumerable<AircraftState> snapshots, double speed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        if (double.IsNaN(speed) || speed <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "The replay speed must be a positive number.");
        }

        AircraftState? previous = null;

        await foreach (var snapshot in snapshots.WithCancellation(ct))
        {
            if (previous is not null)
            {
                var wait = ScaledGap(snapshot.Timestamp - previous.Timestamp, speed);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _timeProvider, ct);
                }
            }

            SetState(snapshot);
            previous = snapshot;
        }
    }

    /// <summary>
    /// Replays a telemetry file written by <see cref="Telemetry.TelemetryRecorder"/>; see
    /// <see cref="ReplayAsync(IAsyncEnumerable{AircraftState}, double, CancellationToken)"/>. The stream is left open.
    /// </summary>
    /// <exception cref="Telemetry.TelemetryFormatException">The file is not valid; snapshots before the problem were already replayed.</exception>
    public Task ReplayAsync(Stream recording, double speed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return ReplayAsync(Telemetry.TelemetryReader.ReadAsync(recording, ct), speed, ct);
    }

    /// <summary>Sets the active frequency of the radio in the current state and publishes the result.</summary>
    /// <exception cref="InvalidOperationException">Not connected, or no state available yet.</exception>
    public Task SetComFrequencyAsync(ComRadio radio, Frequency frequency, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_connectionState != SimConnectionState.Connected)
            {
                throw new InvalidOperationException("The adapter is not connected.");
            }

            Update(state => radio switch
            {
                ComRadio.Com1 => state with { Com1 = Tuned(state.Com1, frequency) },
                ComRadio.Com2 => state with { Com2 = Tuned(state.Com2, frequency) },
                _ => throw new ArgumentOutOfRangeException(nameof(radio), radio, null),
            });
        }

        return Task.CompletedTask;
    }

    // A radio the aircraft did not report starts with the same frequency in active and standby.
    private static RadioState Tuned(RadioState? radio, Frequency frequency)
    {
        return radio is null ? new RadioState(frequency, frequency) : radio with { Active = frequency };
    }

    private static TimeSpan ScaledGap(TimeSpan gap, double speed)
    {
        if (gap <= TimeSpan.Zero || double.IsPositiveInfinity(speed))
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromTicks((long)Math.Min(gap.Ticks / speed, TimeSpan.MaxValue.Ticks));
    }

    // Must be called with _gate held.
    private void Publish(AircraftState state)
    {
        _broadcaster.Publish(state);
    }

    // Must be called with _gate held.
    private void DeliverProgrammedState()
    {
        if (_programmedState is not null)
        {
            Publish(_programmedState);
        }
    }

    // Must be called with _gate held.
    private void ChangeState(SimConnectionState next, List<SimConnectionStateChangedEventArgs> changes)
    {
        changes.Add(new SimConnectionStateChangedEventArgs(_connectionState, next));
        _connectionState = next;
    }

    // Must be called with _gate held. No-op while already disconnected; also ends a reconnection in progress.
    private void Disconnect(List<SimConnectionStateChangedEventArgs> changes)
    {
        if (_connectionState == SimConnectionState.Disconnected)
        {
            return;
        }

        ChangeState(SimConnectionState.Disconnected, changes);

        _broadcaster.CompleteSubscriptions();
    }

    // Handlers run outside the lock so they can call back into the adapter from any thread.
    private void Raise(List<SimConnectionStateChangedEventArgs> changes)
    {
        foreach (var change in changes)
        {
            ConnectionStateChanged?.Invoke(this, change);
        }
    }
}
