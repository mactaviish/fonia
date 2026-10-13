using System.Threading.Channels;

namespace Fonia.Core.Sim;

/// <summary>
/// The streaming half of an <see cref="ISimAdapter"/>, shared by every adapter so they all behave the same:
/// each <see cref="Subscribe"/> is an independent subscription that starts at the call, a slow reader only ever
/// sees the most recent snapshot (so it never blocks the publisher or grows memory), and a stream ends quietly.
/// Thread-safe.
/// </summary>
public sealed class AircraftStateBroadcaster
{
    private readonly Lock _gate = new();
    private readonly List<Subscription> _subscriptions = [];
    private AircraftState? _latest;

    /// <summary>The last snapshot published, or <see langword="null"/> if none yet. Kept after <see cref="CompleteSubscriptions"/>.</summary>
    public AircraftState? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <summary>
    /// Makes <paramref name="state"/> the <see cref="Latest"/> and delivers it to every current subscriber.
    /// </summary>
    public void Publish(AircraftState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_gate)
        {
            _latest = state;

            foreach (var subscription in _subscriptions)
            {
                subscription.Channel.Writer.TryWrite(state);
            }
        }
    }

    /// <summary>
    /// A new subscription to what is published from now on (nothing is replayed). It starts at this call, not at the
    /// first read, and its stream ends when <paramref name="ct"/> is cancelled or on <see cref="CompleteSubscriptions"/>.
    /// </summary>
    public IAsyncEnumerable<AircraftState> Subscribe(CancellationToken ct)
    {
        var subscription = new Subscription();

        lock (_gate)
        {
            _subscriptions.Add(subscription);
        }

        subscription.CancellationRegistration = ct.Register(() => Remove(subscription));

        return Read(subscription);
    }

    /// <summary>
    /// Ends every current stream (after the snapshots already buffered are read). Streams subscribed afterwards
    /// work normally.
    /// </summary>
    public void CompleteSubscriptions()
    {
        lock (_gate)
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Channel.Writer.TryComplete();
            }

            _subscriptions.Clear();
        }
    }

    private void Remove(Subscription subscription)
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
            // The channel is completed on cancellation or completion, so the stream ends quietly
            // instead of throwing; buffered snapshots are still delivered first.
            await foreach (var state in subscription.Channel.Reader.ReadAllAsync(CancellationToken.None))
            {
                yield return state;
            }
        }
        finally
        {
            await subscription.CancellationRegistration.DisposeAsync();
            Remove(subscription);
        }
    }

    private sealed class Subscription
    {
        // Capacity 1 + DropOldest: a slow consumer only ever sees the most recent snapshot and
        // never blocks the publisher or grows memory.
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
