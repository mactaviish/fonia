using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;
using Fonia.Sim.Mock;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.Core.Tests.Sim;

public class ScriptedTelemetryTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<AircraftState> NextAsync(IAsyncEnumerator<AircraftState> enumerator)
    {
        (await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct)).ShouldBeTrue("the stream ended early");
        return enumerator.Current;
    }

    [Fact]
    public async Task PlayAsync_without_interval_publishes_the_whole_script()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        var script = MockAircraft.TaxiSequence(4);
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        await sim.PlayAsync(script, TimeSpan.Zero, Ct);

        // A stream keeps only the most recent snapshot (see MockSimAdapterTests), so the last one wins.
        sim.LatestState.ShouldBe(script[^1]);
        (await NextAsync(stream)).ShouldBe(script[^1]);
    }

    [Fact]
    public async Task PlayAsync_waits_the_interval_between_snapshots()
    {
        // The spy clock says exactly what the code waits for and only then lets the test move time, so the test
        // never guesses how far to advance (guessing is what made it fail on slow machines).
        var clock = new TimerSpyTimeProvider(new FakeTimeProvider());
        await using var sim = new MockSimAdapter(clock);
        await sim.ConnectAsync(Ct);
        var script = MockAircraft.TaxiSequence(3);
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        var play = sim.PlayAsync(script, Interval, Ct);

        // The first snapshot is published immediately, without any delay.
        (await NextAsync(stream)).ShouldBe(script[0]);

        // Then it waits one interval; without advancing the clock nothing else is published.
        (await clock.NextRequestedDelayAsync(Ct)).ShouldBe(Interval);
        var second = stream.MoveNextAsync().AsTask();
        await Task.Delay(50, Ct);
        second.IsCompleted.ShouldBeFalse();
        play.IsCompleted.ShouldBeFalse();

        clock.Advance(Interval);
        (await second.WaitAsync(Timeout, Ct)).ShouldBeTrue();
        stream.Current.ShouldBe(script[1]);

        (await clock.NextRequestedDelayAsync(Ct)).ShouldBe(Interval);
        var third = stream.MoveNextAsync().AsTask();
        clock.Advance(Interval);
        (await third.WaitAsync(Timeout, Ct)).ShouldBeTrue();
        stream.Current.ShouldBe(script[2]);

        await play.WaitAsync(Timeout, Ct);
        play.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task PlayAsync_cancelled_while_waiting_throws_and_keeps_published_snapshots()
    {
        var clock = new TimerSpyTimeProvider(new FakeTimeProvider());
        await using var sim = new MockSimAdapter(clock);
        await sim.ConnectAsync(Ct);
        var script = MockAircraft.TaxiSequence(3);
        using var cts = new CancellationTokenSource();

        var play = sim.PlayAsync(script, Interval, cts.Token);
        await clock.NextRequestedDelayAsync(Ct); // it is now waiting for the second snapshot

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => play.WaitAsync(Timeout, Ct));
        sim.LatestState.ShouldBe(script[0]);
    }

    [Fact]
    public async Task PlayAsync_with_an_empty_script_publishes_nothing()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);

        await sim.PlayAsync([], Interval, Ct);

        sim.LatestState.ShouldBeNull();
    }

    [Fact]
    public async Task PlayAsync_rejects_invalid_arguments()
    {
        await using var sim = new MockSimAdapter();

        await Should.ThrowAsync<ArgumentNullException>(() => sim.PlayAsync(null!, Interval, Ct));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => sim.PlayAsync(MockAircraft.TaxiSequence(2), TimeSpan.FromSeconds(-1), Ct));
    }

    [Fact]
    public void TaxiSequence_accelerates_from_standstill_to_ten_knots_one_second_apart()
    {
        var sequence = MockAircraft.TaxiSequence(5);

        sequence.Count.ShouldBe(5);
        sequence[0].GroundSpeedKnots.ShouldBe(0);
        sequence[^1].GroundSpeedKnots.ShouldBe(10);
        sequence.Select(s => s.GroundSpeedKnots).ShouldBeInOrder();
        sequence[1].Timestamp.ShouldBe(sequence[0].Timestamp.AddSeconds(1));
        sequence.ShouldAllBe(s => s.OnGround);
    }

    [Fact]
    public void TaxiSequence_requires_at_least_one_step()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => MockAircraft.TaxiSequence(0));
        MockAircraft.TaxiSequence(1).Single().GroundSpeedKnots.ShouldBe(10);
    }
}
