using Fonia.Core.Sim;
using Fonia.Sim.Mock;

namespace Fonia.Core.Tests.Sim;

public class AircraftStateBroadcasterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AircraftState Speed(double knots) => MockAircraft.Parked() with { GroundSpeedKnots = knots };

    private static async Task<AircraftState> NextAsync(IAsyncEnumerator<AircraftState> enumerator)
    {
        (await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct)).ShouldBeTrue("the stream ended early");
        return enumerator.Current;
    }

    [Fact]
    public void Latest_is_null_until_something_is_published()
    {
        var broadcaster = new AircraftStateBroadcaster();

        broadcaster.Latest.ShouldBeNull();

        broadcaster.Publish(Speed(1));

        broadcaster.Latest.ShouldBe(Speed(1));
    }

    [Fact]
    public async Task A_subscriber_receives_what_is_published_after_subscribing()
    {
        var broadcaster = new AircraftStateBroadcaster();
        await using var stream = broadcaster.Subscribe(Ct).GetAsyncEnumerator(Ct);

        broadcaster.Publish(Speed(5));

        (await NextAsync(stream)).ShouldBe(Speed(5));
    }

    [Fact]
    public async Task The_subscription_starts_at_the_call_not_at_the_first_read()
    {
        var broadcaster = new AircraftStateBroadcaster();
        var sequence = broadcaster.Subscribe(Ct);

        broadcaster.Publish(Speed(7)); // before anyone enumerates

        await using var stream = sequence.GetAsyncEnumerator(Ct);
        (await NextAsync(stream)).ShouldBe(Speed(7));
    }

    [Fact]
    public async Task What_was_published_before_subscribing_is_not_replayed()
    {
        var broadcaster = new AircraftStateBroadcaster();
        broadcaster.Publish(Speed(1));
        await using var stream = broadcaster.Subscribe(Ct).GetAsyncEnumerator(Ct);

        broadcaster.Publish(Speed(2));

        (await NextAsync(stream)).ShouldBe(Speed(2));
    }

    [Fact]
    public async Task Subscribers_are_independent()
    {
        var broadcaster = new AircraftStateBroadcaster();
        await using var first = broadcaster.Subscribe(Ct).GetAsyncEnumerator(Ct);
        await using var second = broadcaster.Subscribe(Ct).GetAsyncEnumerator(Ct);

        broadcaster.Publish(Speed(3));

        (await NextAsync(first)).ShouldBe(Speed(3));
        (await NextAsync(second)).ShouldBe(Speed(3));
    }

    [Fact]
    public async Task A_slow_subscriber_only_sees_the_most_recent_snapshot()
    {
        var broadcaster = new AircraftStateBroadcaster();
        await using var stream = broadcaster.Subscribe(Ct).GetAsyncEnumerator(Ct);

        broadcaster.Publish(Speed(1));
        broadcaster.Publish(Speed(2));
        broadcaster.Publish(Speed(3));

        (await NextAsync(stream)).ShouldBe(Speed(3));
    }

    [Fact]
    public async Task Completing_ends_every_stream_but_keeps_the_latest_snapshot()
    {
        var broadcaster = new AircraftStateBroadcaster();
        await using var stream = broadcaster.Subscribe(Ct).GetAsyncEnumerator(Ct);
        broadcaster.Publish(Speed(4));

        broadcaster.CompleteSubscriptions();

        (await NextAsync(stream)).ShouldBe(Speed(4)); // what was already buffered is still delivered
        (await stream.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct)).ShouldBeFalse();
        broadcaster.Latest.ShouldBe(Speed(4));
    }

    [Fact]
    public async Task Subscriptions_made_after_completing_work_normally()
    {
        var broadcaster = new AircraftStateBroadcaster();
        broadcaster.CompleteSubscriptions();
        await using var stream = broadcaster.Subscribe(Ct).GetAsyncEnumerator(Ct);

        broadcaster.Publish(Speed(9));

        (await NextAsync(stream)).ShouldBe(Speed(9));
    }

    [Fact]
    public async Task Cancelling_the_token_ends_the_stream_without_throwing()
    {
        var broadcaster = new AircraftStateBroadcaster();
        using var cts = new CancellationTokenSource();
        await using var stream = broadcaster.Subscribe(cts.Token).GetAsyncEnumerator(Ct);

        await cts.CancelAsync();

        (await stream.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_token_that_is_already_cancelled_gives_an_empty_stream()
    {
        var broadcaster = new AircraftStateBroadcaster();

        await using var stream = broadcaster.Subscribe(new CancellationToken(canceled: true)).GetAsyncEnumerator(Ct);
        broadcaster.Publish(Speed(1));

        (await stream.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct)).ShouldBeFalse();
    }

    [Fact]
    public void Publishing_without_subscribers_is_fine_and_null_is_rejected()
    {
        var broadcaster = new AircraftStateBroadcaster();

        Should.NotThrow(() => broadcaster.Publish(Speed(1)));
        Should.Throw<ArgumentNullException>(() => broadcaster.Publish(null!));
    }

    [Fact]
    public async Task Publishing_subscribing_and_completing_from_many_threads_stays_consistent()
    {
        var broadcaster = new AircraftStateBroadcaster();
        using var stop = new CancellationTokenSource();

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(
            async () =>
            {
                await foreach (var state in broadcaster.Subscribe(stop.Token))
                {
                    state.ShouldNotBeNull();
                }
            },
            Ct)).ToArray();

        Parallel.For(0, 4000, i =>
        {
            if (i % 500 == 0)
            {
                broadcaster.CompleteSubscriptions();
            }
            else
            {
                broadcaster.Publish(Speed(i));
            }
        });

        await stop.CancelAsync();
        await Task.WhenAll(readers).WaitAsync(Timeout, Ct);
    }
}
