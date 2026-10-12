using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;
using Fonia.Sim.Mock;
using Fonia.Sim.Mock.Telemetry;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.Core.Tests.Telemetry;

public class ReplayTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Snapshots whose timestamps are the given number of seconds after a common start.</summary>
    private static AircraftState[] At(params double[] secondsFromStart)
    {
        var start = MockAircraft.Parked().Timestamp;
        return [.. secondsFromStart.Select((s, i) => MockAircraft.Parked() with
        {
            Timestamp = start.AddSeconds(s),
            GroundSpeedKnots = i,
        })];
    }

    private static async IAsyncEnumerable<AircraftState> AsAsync(IEnumerable<AircraftState> states)
    {
        foreach (var state in states)
        {
            yield return state;
            await Task.Yield();
        }
    }

    private static async Task<AircraftState> NextAsync(IAsyncEnumerator<AircraftState> enumerator)
    {
        (await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct)).ShouldBeTrue("the stream ended early");
        return enumerator.Current;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(Timeout);

        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    [Theory]
    [InlineData(1.0, 1.0, 2.0)]
    [InlineData(2.0, 0.5, 1.0)]
    [InlineData(0.5, 2.0, 4.0)]
    public async Task Replay_waits_the_gap_between_timestamps_divided_by_the_speed(double speed, double firstGap, double secondGap)
    {
        var clock = new TimerSpyTimeProvider(new FakeTimeProvider());
        await using var sim = new MockSimAdapter(clock);
        await sim.ConnectAsync(Ct);
        var script = At(0, 1, 3); // gaps of 1 s and 2 s in the recording

        var replay = sim.ReplayAsync(AsAsync(script), speed, Ct);

        (await clock.NextRequestedDelayAsync(Ct)).ShouldBe(TimeSpan.FromSeconds(firstGap));
        clock.Advance(TimeSpan.FromSeconds(firstGap));
        (await clock.NextRequestedDelayAsync(Ct)).ShouldBe(TimeSpan.FromSeconds(secondGap));
        clock.Advance(TimeSpan.FromSeconds(secondGap));
        await replay.WaitAsync(Timeout, Ct);

        sim.LatestState.ShouldBe(script[2]);
    }

    [Fact]
    public async Task Replay_publishes_each_snapshot_only_when_its_time_comes()
    {
        var clock = new TimerSpyTimeProvider(new FakeTimeProvider());
        await using var sim = new MockSimAdapter(clock);
        await sim.ConnectAsync(Ct);
        var script = At(0, 5, 6);
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        var replay = sim.ReplayAsync(AsAsync(script), 1.0, Ct);

        // The first snapshot goes out immediately; the second waits for its 5 s.
        (await NextAsync(stream)).ShouldBe(script[0]);
        (await clock.NextRequestedDelayAsync(Ct)).ShouldBe(TimeSpan.FromSeconds(5));
        sim.LatestState.ShouldBe(script[0]);
        replay.IsCompleted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromSeconds(5));
        (await NextAsync(stream)).ShouldBe(script[1]);

        (await clock.NextRequestedDelayAsync(Ct)).ShouldBe(TimeSpan.FromSeconds(1));
        clock.Advance(TimeSpan.FromSeconds(1));
        (await NextAsync(stream)).ShouldBe(script[2]);
        await replay.WaitAsync(Timeout, Ct);
    }

    [Fact]
    public async Task Replay_does_not_wait_for_equal_or_backwards_timestamps()
    {
        var clock = new TimerSpyTimeProvider(new FakeTimeProvider());
        await using var sim = new MockSimAdapter(clock);
        await sim.ConnectAsync(Ct);
        var script = At(10, 10, 4, 4);

        await sim.ReplayAsync(AsAsync(script), 1.0, Ct).WaitAsync(Timeout, Ct);

        clock.HasPendingRequest.ShouldBeFalse();
        sim.LatestState.ShouldBe(script[3]);
    }

    [Fact]
    public async Task Replay_at_infinite_speed_does_not_wait()
    {
        var clock = new TimerSpyTimeProvider(new FakeTimeProvider());
        await using var sim = new MockSimAdapter(clock);
        await sim.ConnectAsync(Ct);
        var script = At(0, 60, 3600);

        await sim.ReplayAsync(AsAsync(script), double.PositiveInfinity, Ct).WaitAsync(Timeout, Ct);

        clock.HasPendingRequest.ShouldBeFalse();
        sim.LatestState.ShouldBe(script[2]);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public async Task Replay_rejects_a_speed_that_is_not_positive(double speed)
    {
        await using var sim = new MockSimAdapter();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => sim.ReplayAsync(AsAsync(At(0, 1)), speed, Ct));
    }

    [Fact]
    public async Task Replay_with_an_empty_recording_publishes_nothing()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);

        await sim.ReplayAsync(AsAsync([]), 1.0, Ct);

        sim.LatestState.ShouldBeNull();
    }

    [Fact]
    public async Task Cancelling_during_a_wait_throws_and_keeps_what_was_published()
    {
        var clock = new TimerSpyTimeProvider(new FakeTimeProvider());
        await using var sim = new MockSimAdapter(clock);
        await sim.ConnectAsync(Ct);
        var script = At(0, 10);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var replay = sim.ReplayAsync(AsAsync(script), 1.0, cts.Token);
        await clock.NextRequestedDelayAsync(Ct);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => replay.WaitAsync(Timeout, Ct));
        sim.LatestState.ShouldBe(script[0]);
    }

    [Fact]
    public async Task Replay_from_a_recording_stream_plays_the_file()
    {
        var script = MockAircraft.TaxiSequence(4); // one second apart
        var file = new MemoryStream();
        await using (var writer = await TelemetryWriter.CreateAsync(file, leaveOpen: true, Ct))
        {
            foreach (var state in script)
            {
                await writer.WriteAsync(state, Ct);
            }
        }

        file.Position = 0;
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);

        await sim.ReplayAsync(file, double.PositiveInfinity, Ct).WaitAsync(Timeout, Ct);

        sim.LatestState.ShouldBe(script[^1]);
        file.CanRead.ShouldBeTrue();
    }

    [Fact]
    public async Task Replay_from_a_bad_file_fails_after_playing_the_valid_part()
    {
        var file = new MemoryStream();
        await using (var writer = await TelemetryWriter.CreateAsync(file, leaveOpen: true, Ct))
        {
            await writer.WriteAsync(MockAircraft.Parked(), Ct);
        }

        file.Seek(0, SeekOrigin.End);
        await file.WriteAsync("{ broken\n"u8.ToArray(), Ct);
        file.Position = 0;
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);

        var error = await Should.ThrowAsync<TelemetryFormatException>(() => sim.ReplayAsync(file, double.PositiveInfinity, Ct));

        error.LineNumber.ShouldBe(3);
        sim.LatestState.ShouldBe(MockAircraft.Parked());
    }

    [Fact]
    public async Task A_recorded_session_replays_to_the_same_snapshots_at_the_original_pace()
    {
        // Record a session from one adapter...
        var original = MockAircraft.TaxiSequence(4);
        var file = new MemoryStream();
        await using (var source = new MockSimAdapter())
        {
            await source.ConnectAsync(Ct);
            var recorder = new TelemetryRecorder(source, file, leaveOpen: true);
            using var stop = new CancellationTokenSource();
            var recording = recorder.RunAsync(stop.Token);

            for (var i = 0; i < original.Count; i++)
            {
                source.SetState(original[i]);
                var expected = i + 1;
                await WaitUntilAsync(() => recorder.Count >= expected);
            }

            await stop.CancelAsync();
            await recording.WaitAsync(Timeout, Ct);
        }

        file.Position = 0;

        // ...and play it back into another one, second by second, without the simulator.
        var clock = new TimerSpyTimeProvider(new FakeTimeProvider());
        await using var replayed = new MockSimAdapter(clock);
        await replayed.ConnectAsync(Ct);
        await using var stream = replayed.StreamAsync(Ct).GetAsyncEnumerator(Ct);
        var replay = replayed.ReplayAsync(file, 1.0, Ct);

        var received = new List<AircraftState> { await NextAsync(stream) };
        while (received.Count < original.Count)
        {
            (await clock.NextRequestedDelayAsync(Ct)).ShouldBe(TimeSpan.FromSeconds(1));
            clock.Advance(TimeSpan.FromSeconds(1));
            received.Add(await NextAsync(stream));
        }

        await replay.WaitAsync(Timeout, Ct);
        received.ShouldBe(original);
    }
}
