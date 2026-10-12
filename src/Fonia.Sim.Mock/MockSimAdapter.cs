using System.Threading.Channels;
using Fonia.Core.Sim;

namespace Fonia.Sim.Mock;

/// <summary>
/// In-memory <see cref="ISimAdapter"/> whose state is programmed by the caller. It is the base of
/// every automated test: no simulator, no timing, no I/O. Thread-safe.
/// </summary>
/// <remarks>
/// <see cref="SetState"/> / <see cref="Update"/> play the role of the simulator producing a snapshot.
/// A state programmed before <see cref="ConnectAsync"/> is delivered as the first snapshot on connect.
/// </remarks>
public sealed class MockSimAdapter : ISimAdapter, IComRadioControl
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly List<Subscription> _subscriptions = [];
    private SimConnectionState _connectionState = SimConnectionState.Disconnected;
    private AircraftState? _latestState;
    private AircraftState? _programmedState;
    private bool _disposed;

    /// <param name="timeProvider">Clock used for the delays of <see cref="PlayAsync"/>; defaults to the system clock.</param>
    public MockSimAdapter(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

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
                return _latestState;
            }
        }
    }

    public Task ConnectAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_connectionState == SimConnectionState.Connected)
            {
                return Task.CompletedTask;
            }

            _connectionState = SimConnectionState.Connected;

            if (_programmedState is not null)
            {
                Publish(_programmedState);
            }
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            Disconnect();
        }

        return Task.CompletedTask;
    }

    public IAsyncEnumerable<AircraftState> StreamAsync(CancellationToken ct)
    {
        Subscription subscription;

        // Subscribe eagerly (not on first MoveNext) so that no snapshot published after this call is missed.
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            subscription = new Subscription();
            _subscriptions.Add(subscription);
        }

        subscription.CancellationRegistration = ct.Register(() => Unsubscribe(subscription));

        return Read(subscription);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            Disconnect();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Programs a new snapshot. While connected it becomes <see cref="LatestState"/> and is delivered
    /// to every stream; while disconnected it is kept and delivered as the first snapshot on connect.
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
    /// Programs a new snapshot derived from the current one (<see cref="LatestState"/> while connected,
    /// otherwise the last programmed state).
    /// </summary>
    /// <exception cref="InvalidOperationException">No state has been programmed yet.</exception>
    public void Update(Func<AircraftState, AircraftState> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (_gate)
        {
            var current = _latestState ?? _programmedState
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
                ComRadio.Com1 => state with { Com1 = state.Com1 with { Active = frequency } },
                ComRadio.Com2 => state with { Com2 = state.Com2 with { Active = frequency } },
                _ => throw new ArgumentOutOfRangeException(nameof(radio), radio, null),
            });
        }

        return Task.CompletedTask;
    }

    // Must be called with _gate held.
    private void Publish(AircraftState state)
    {
        _latestState = state;

        foreach (var subscription in _subscriptions)
        {
            subscription.Channel.Writer.TryWrite(state);
        }
    }

    // Must be called with _gate held. No-op while already disconnected.
    private void Disconnect()
    {
        if (_connectionState == SimConnectionState.Disconnected)
        {
            return;
        }

        _connectionState = SimConnectionState.Disconnected;

        foreach (var subscription in _subscriptions)
        {
            subscription.Channel.Writer.TryComplete();
        }

        _subscriptions.Clear();
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_gate)
        {
            _subscriptions.Remove(subscription);
        }

        subscription.Channel.Writer.TryComplete();
    }

    private async IAsyncEnumerable<AircraftState> Read(Subscription subscription)
    {
        try
        {
            // The channel is completed on cancellation / disconnect / dispose, so the stream ends
            // quietly instead of throwing; buffered snapshots are still delivered first.
            await foreach (var state in subscription.Channel.Reader.ReadAllAsync(CancellationToken.None))
            {
                yield return state;
            }
        }
        finally
        {
            await subscription.CancellationRegistration.DisposeAsync();
            Unsubscribe(subscription);
        }
    }

    private sealed class Subscription
    {
        // Capacity 1 + DropOldest: a slow consumer only ever sees the most recent snapshot and
        // never blocks the adapter or grows memory.
        public Channel<AircraftState> Channel { get; } = System.Threading.Channels.Channel.CreateBounded<AircraftState>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

        public CancellationTokenRegistration CancellationRegistration { get; set; }
    }
}
