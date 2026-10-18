using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;
using Fonia.Sim.Msfs.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.Sim.Msfs.Tests;

public class MsfsSimAdapterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>An adapter wired to scripted sessions and a clock that reports every wait it is asked for.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly List<(SimConnectionState Previous, SimConnectionState Current)> _changes = [];

        public Harness(Func<int, FakeSession> script, MsfsAdapterOptions? options = null)
        {
            Clock = new TimerSpyTimeProvider(new FakeTimeProvider());
            Factory = new FakeSessionFactory(script);
            Adapter = new MsfsSimAdapter(Factory, NullLogger<MsfsSimAdapter>.Instance, Clock, options ?? new MsfsAdapterOptions());
            Adapter.ConnectionStateChanged += (_, e) =>
            {
                lock (_gate)
                {
                    _changes.Add((e.Previous, e.Current));
                }
            };
        }

        public TimerSpyTimeProvider Clock { get; }

        public FakeSessionFactory Factory { get; }

        public MsfsSimAdapter Adapter { get; }

        public IReadOnlyList<(SimConnectionState Previous, SimConnectionState Current)> Changes
        {
            get
            {
                lock (_gate)
                {
                    return [.. _changes];
                }
            }
        }

        /// <summary>Waits for the adapter to ask for a pause, checks its length, and lets that time pass.</summary>
        public async Task TickAsync(TimeSpan expected)
        {
            var requested = await Clock.NextRequestedDelayAsync(Ct);
            requested.ShouldBe(expected);
            Clock.Advance(requested);
        }

        public async Task<FakeSession> ConnectedFirstSessionAsync()
        {
            await Adapter.ConnectAsync(Ct).WaitAsync(Timeout, Ct);
            return Factory.Created[0];
        }

        public ValueTask DisposeAsync() => Adapter.DisposeAsync();
    }

    private static FakeSession Healthy() => FakeSessionFactory.Healthy();

    private static FakeSession Unavailable() => FakeSessionFactory.Unavailable();

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    private static async Task<AircraftState> NextAsync(IAsyncEnumerator<AircraftState> enumerator)
    {
        (await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct)).ShouldBeTrue("the stream ended early");
        return enumerator.Current;
    }

    // ---- connecting ----

    [Fact]
    public async Task A_new_adapter_is_disconnected_and_has_no_state()
    {
        await using var h = new Harness(_ => Healthy());

        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
        h.Adapter.LatestState.ShouldBeNull();
        h.Factory.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task Connecting_goes_through_connecting_to_connected()
    {
        await using var h = new Harness(_ => Healthy());

        await h.Adapter.ConnectAsync(Ct).WaitAsync(Timeout, Ct);

        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Connected);
        h.Changes.ShouldBe(
        [
            (SimConnectionState.Disconnected, SimConnectionState.Connecting),
            (SimConnectionState.Connecting, SimConnectionState.Connected),
        ]);
        h.Factory.Created.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Connect_keeps_retrying_with_growing_pauses_until_msfs_is_reachable()
    {
        await using var h = new Harness(i => i < 3 ? Unavailable() : Healthy());

        var connect = h.Adapter.ConnectAsync(Ct);
        await h.TickAsync(Seconds(2));
        await h.TickAsync(Seconds(4));
        await h.TickAsync(Seconds(8));
        await connect.WaitAsync(Timeout, Ct);

        h.Factory.Created.Count.ShouldBe(4);
        h.Factory.Created.Take(3).ShouldAllBe(s => s.Disposed);
        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Connected);
    }

    [Fact]
    public async Task The_pause_between_attempts_stops_growing_at_the_maximum()
    {
        await using var h = new Harness(i => i < 6 ? Unavailable() : Healthy());

        var connect = h.Adapter.ConnectAsync(Ct);
        foreach (var expected in new[] { 2, 4, 8, 10, 10, 10 })
        {
            await h.TickAsync(Seconds(expected));
        }

        await connect.WaitAsync(Timeout, Ct);
    }

    [Fact]
    public async Task Connect_does_not_complete_while_msfs_is_unreachable()
    {
        await using var h = new Harness(_ => Unavailable());

        var connect = h.Adapter.ConnectAsync(Ct);
        await h.TickAsync(Seconds(2));
        await h.TickAsync(Seconds(4));

        connect.IsCompleted.ShouldBeFalse();
        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Connecting);
    }

    [Fact]
    public async Task Cancelling_connect_while_retrying_stops_everything_and_goes_back_to_disconnected()
    {
        await using var h = new Harness(_ => Unavailable());
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var connect = h.Adapter.ConnectAsync(cts.Token);
        await h.Clock.NextRequestedDelayAsync(Ct);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => connect.WaitAsync(Timeout, Ct));
        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
        h.Changes[^1].ShouldBe((SimConnectionState.Connecting, SimConnectionState.Disconnected));
        h.Factory.Created.Count.ShouldBe(1);
        h.Factory.Created[0].Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unexpected_error_while_opening_is_retried_not_fatal()
    {
        await using var h = new Harness(i => i == 0
            ? new FakeSession { OnOpen = _ => Task.FromException(new InvalidOperationException("boom")) }
            : Healthy());

        var connect = h.Adapter.ConnectAsync(Ct);
        await h.TickAsync(Seconds(2));
        await connect.WaitAsync(Timeout, Ct);

        h.Factory.Created[0].Disposed.ShouldBeTrue();
        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Connected);
    }

    [Fact]
    public async Task A_failure_to_subscribe_discards_the_session_and_retries()
    {
        await using var h = new Harness(i => i == 0
            ? new FakeSession { OnSubscribe = () => Task.FromException(new InvalidOperationException("rejected")) }
            : Healthy());

        var connect = h.Adapter.ConnectAsync(Ct);
        await h.TickAsync(Seconds(2));
        await connect.WaitAsync(Timeout, Ct);

        h.Factory.Created[0].Disposed.ShouldBeTrue();
        h.Factory.Created.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Connect_when_already_connected_does_nothing()
    {
        await using var h = new Harness(_ => Healthy());
        await h.Adapter.ConnectAsync(Ct).WaitAsync(Timeout, Ct);

        await h.Adapter.ConnectAsync(Ct).WaitAsync(Timeout, Ct);

        h.Factory.Created.Count.ShouldBe(1);
        h.Changes.Count.ShouldBe(2);
    }

    // ---- data ----

    [Fact]
    public async Task Data_from_the_session_reaches_the_streams_and_latest_state()
    {
        await using var h = new Harness(_ => Healthy());
        var session = await h.ConnectedFirstSessionAsync();
        await using var stream = h.Adapter.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        session.PlayFlight(registration: "PR-FAB");

        var state = await NextAsync(stream);
        state.Registration.ShouldBe("PR-FAB");
        state.AircraftType.ShouldBe("C25C");
        state.Com1.ShouldBe(new RadioState(new Frequency(130850), new Frequency(124850)));
        state.TransponderCode.ShouldBe(1200);
        h.Adapter.LatestState.ShouldBe(state);
    }

    [Fact]
    public async Task Nothing_is_published_while_the_simulation_is_not_running()
    {
        await using var h = new Harness(_ => Healthy());
        var session = await h.ConnectedFirstSessionAsync();
        var sink = session.Sink!;

        sink.OnIdentity(new IdentityData("PR-FAB", "Cessna", "C25C", "CJ4"));
        sink.OnRadios(new RadioData(130_850_000, 124_850_000, 124_850_000, 124_850_000));
        sink.OnTransponder(new TransponderData(0x1200));
        sink.OnMotion(new MotionData(-27.1, -52.6, 5675, 3637, 79, 21, 0));

        h.Adapter.LatestState.ShouldBeNull();
    }

    // ---- losing the connection ----

    [Fact]
    public async Task Two_dead_probes_in_a_row_rebuild_the_connection_and_the_streams_survive()
    {
        await using var h = new Harness(_ => Healthy());
        var first = await h.ConnectedFirstSessionAsync();
        await using var stream = h.Adapter.StreamAsync(Ct).GetAsyncEnumerator(Ct);
        first.PlayFlight(registration: "PR-FAB");
        (await NextAsync(stream)).Registration.ShouldBe("PR-FAB");
        first.QueueProbes(SessionHealth.Dead, SessionHealth.Dead);

        await h.TickAsync(Seconds(3)); // first dead probe: nothing happens yet
        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Connected);
        await h.TickAsync(Seconds(3)); // second one: the connection is gone

        await h.Factory.WaitForSessionsAsync(2, Ct);
        await h.Clock.NextRequestedDelayAsync(Ct); // the new session is up and probing again

        first.Disposed.ShouldBeTrue();
        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Connected);
        h.Changes.TakeLast(2).ShouldBe(
        [
            (SimConnectionState.Connected, SimConnectionState.Connecting),
            (SimConnectionState.Connecting, SimConnectionState.Connected),
        ]);

        // The same stream carries on with data from the new session.
        h.Factory.Created[1].PlayFlight(registration: "PS-WAS");
        (await NextAsync(stream)).Registration.ShouldBe("PS-WAS");
    }

    [Fact]
    public async Task While_the_connection_is_lost_the_state_is_kept_and_the_streams_stay_open()
    {
        await using var h = new Harness(i => i == 0 ? Healthy() : Unavailable());
        var first = await h.ConnectedFirstSessionAsync();
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await using var stream = h.Adapter.StreamAsync(streamCts.Token).GetAsyncEnumerator(Ct);
        first.PlayFlight();
        var before = await NextAsync(stream);
        first.QueueProbes(SessionHealth.Dead, SessionHealth.Dead);

        await h.TickAsync(Seconds(3));
        await h.TickAsync(Seconds(3));
        await h.Clock.NextRequestedDelayAsync(Ct); // now retrying: the new sessions are unreachable

        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Connecting);
        h.Adapter.LatestState.ShouldBe(before);
        var pending = stream.MoveNextAsync().AsTask();
        await Task.Delay(50, Ct);
        pending.IsCompleted.ShouldBeFalse();

        // End the stream ourselves: an iterator cannot be disposed while a read is pending.
        await streamCts.CancelAsync();
        (await pending.WaitAsync(Timeout, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Values_from_before_the_loss_are_never_mixed_into_the_new_session()
    {
        await using var h = new Harness(_ => Healthy());
        var first = await h.ConnectedFirstSessionAsync();
        await using var stream = h.Adapter.StreamAsync(Ct).GetAsyncEnumerator(Ct);
        first.PlayFlight(registration: "PR-FAB");
        await NextAsync(stream);
        first.QueueProbes(SessionHealth.Dead, SessionHealth.Dead);
        await h.TickAsync(Seconds(3));
        await h.TickAsync(Seconds(3));
        await h.Factory.WaitForSessionsAsync(2, Ct);
        await h.Clock.NextRequestedDelayAsync(Ct);

        // The new session only reports motion: the identity, radios and transponder of the old one are gone.
        var second = h.Factory.Created[1];
        second.Sink!.OnSimRunning(true);
        second.Sink.OnMotion(new MotionData(-27.2, -52.7, 5000, 3000, 80, 90, 0));
        var pending = stream.MoveNextAsync().AsTask();
        await Task.Delay(50, Ct);

        pending.IsCompleted.ShouldBeFalse();

        // Once the new session has reported everything, it is published, with the new values.
        second.PlayFlight(registration: "PS-WAS");
        (await pending.WaitAsync(Timeout, Ct)).ShouldBeTrue();
        stream.Current.Registration.ShouldBe("PS-WAS");
    }

    [Fact]
    public async Task Data_from_a_session_that_was_thrown_away_is_ignored()
    {
        await using var h = new Harness(_ => Healthy());
        var first = await h.ConnectedFirstSessionAsync();
        first.QueueProbes(SessionHealth.Dead, SessionHealth.Dead);
        await h.TickAsync(Seconds(3));
        await h.TickAsync(Seconds(3));
        await h.Factory.WaitForSessionsAsync(2, Ct);
        await h.Clock.NextRequestedDelayAsync(Ct);

        first.PlayFlight(registration: "GHOST");

        h.Adapter.LatestState.ShouldBeNull();
    }

    [Fact]
    public async Task A_simulator_that_is_busy_never_counts_as_a_lost_connection()
    {
        await using var h = new Harness(_ => Healthy());
        var session = await h.ConnectedFirstSessionAsync();
        session.QueueProbes(Enumerable.Repeat(SessionHealth.NotResponding, 6).ToArray());

        for (var i = 0; i < 6; i++)
        {
            await h.TickAsync(Seconds(3));
        }

        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Connected);
        h.Factory.Created.Count.ShouldBe(1);
        session.Disposed.ShouldBeFalse();
    }

    [Fact]
    public async Task Dead_probes_that_are_not_in_a_row_do_not_count()
    {
        await using var h = new Harness(_ => Healthy());
        var session = await h.ConnectedFirstSessionAsync();
        session.QueueProbes(SessionHealth.Dead, SessionHealth.Alive, SessionHealth.Dead, SessionHealth.NotResponding, SessionHealth.Dead);

        for (var i = 0; i < 5; i++)
        {
            await h.TickAsync(Seconds(3));
        }

        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Connected);
        h.Factory.Created.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_fault_reported_by_the_session_rebuilds_it_without_waiting_for_probes()
    {
        await using var h = new Harness(_ => Healthy());
        var first = await h.ConnectedFirstSessionAsync();

        first.Sink!.OnFault("SimConnect rejected a request (UnrecognizedId)");

        await h.Factory.WaitForSessionsAsync(2, Ct);
        await h.Clock.NextRequestedDelayAsync(Ct);
        first.Disposed.ShouldBeTrue();
        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Connected);
    }

    [Fact]
    public async Task After_a_loss_the_pauses_start_again_from_the_shortest()
    {
        // First connection after two failures (2 s, 4 s), then a loss, then two failures again: 2 s, 4 s - not 8 s.
        await using var h = new Harness(i => i is 0 or 1 or 3 or 4 ? Unavailable() : Healthy());
        var connect = h.Adapter.ConnectAsync(Ct);
        await h.TickAsync(Seconds(2));
        await h.TickAsync(Seconds(4));
        await connect.WaitAsync(Timeout, Ct);
        var connected = h.Factory.Created[2];
        connected.QueueProbes(SessionHealth.Dead, SessionHealth.Dead);

        await h.TickAsync(Seconds(3));
        await h.TickAsync(Seconds(3));

        await h.TickAsync(Seconds(2));
        await h.TickAsync(Seconds(4));
    }

    // ---- stopping ----

    [Fact]
    public async Task Disconnect_ends_the_streams_disposes_the_session_and_keeps_the_last_state()
    {
        await using var h = new Harness(_ => Healthy());
        var session = await h.ConnectedFirstSessionAsync();
        await using var stream = h.Adapter.StreamAsync(Ct).GetAsyncEnumerator(Ct);
        session.PlayFlight();
        var last = await NextAsync(stream);

        await h.Adapter.DisconnectAsync(Ct);

        (await stream.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct)).ShouldBeFalse();
        session.Disposed.ShouldBeTrue();
        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
        h.Adapter.LatestState.ShouldBe(last);
        h.Changes[^1].ShouldBe((SimConnectionState.Connected, SimConnectionState.Disconnected));
    }

    [Fact]
    public async Task Disconnect_while_retrying_stops_the_retries()
    {
        await using var h = new Harness(_ => Unavailable());
        _ = h.Adapter.ConnectAsync(Ct);
        await h.Clock.NextRequestedDelayAsync(Ct);

        await h.Adapter.DisconnectAsync(Ct);

        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
        h.Factory.Created.Count.ShouldBe(1);
        h.Clock.HasPendingRequest.ShouldBeFalse();
    }

    [Fact]
    public async Task The_adapter_can_connect_again_after_a_disconnect()
    {
        await using var h = new Harness(_ => Healthy());
        await h.ConnectedFirstSessionAsync();
        await h.Adapter.DisconnectAsync(Ct);

        await h.Adapter.ConnectAsync(Ct).WaitAsync(Timeout, Ct);

        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Connected);
        h.Factory.Created.Count.ShouldBe(2);
        await using var stream = h.Adapter.StreamAsync(Ct).GetAsyncEnumerator(Ct);
        h.Factory.Created[1].PlayFlight(registration: "PS-WAS");
        (await NextAsync(stream)).Registration.ShouldBe("PS-WAS");
    }

    [Fact]
    public async Task Dispose_is_idempotent_stops_everything_and_rejects_further_use()
    {
        var h = new Harness(_ => Healthy());
        var session = await h.ConnectedFirstSessionAsync();
        await using var stream = h.Adapter.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        await h.DisposeAsync();
        await h.DisposeAsync();

        (await stream.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct)).ShouldBeFalse();
        session.Disposed.ShouldBeTrue();
        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
        await Should.ThrowAsync<ObjectDisposedException>(() => h.Adapter.ConnectAsync(Ct));
        Should.Throw<ObjectDisposedException>(() => h.Adapter.StreamAsync(Ct));
    }

    [Fact]
    public async Task Dispose_while_retrying_does_not_hang()
    {
        var h = new Harness(_ => Unavailable());
        var connect = h.Adapter.ConnectAsync(Ct);
        await h.Clock.NextRequestedDelayAsync(Ct);

        await h.DisposeAsync().AsTask().WaitAsync(Timeout, Ct);

        await Should.ThrowAsync<OperationCanceledException>(() => connect.WaitAsync(Timeout, Ct));
        h.Adapter.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
    }

    // ---- events and setup ----

    [Fact]
    public async Task State_change_handlers_run_outside_the_adapter_lock()
    {
        await using var h = new Harness(_ => Healthy());
        var observed = new List<SimConnectionState>();
        h.Adapter.ConnectionStateChanged += (_, _) =>
        {
            var read = Task.Run(() => h.Adapter.ConnectionState, Ct);
            SpinWait.SpinUntil(() => read.IsCompleted, Timeout).ShouldBeTrue("the adapter lock was held while raising the event");
            observed.Add(read.Result);
        };

        await h.Adapter.ConnectAsync(Ct).WaitAsync(Timeout, Ct);

        observed.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(0, 10, 3, 2)]
    [InlineData(2, 1, 3, 2)] // maximum below the initial pause
    [InlineData(2, 10, 0, 2)]
    [InlineData(2, 10, 3, 0)]
    public void Invalid_options_are_rejected(int initialSeconds, int maxSeconds, int probeSeconds, int deadProbes)
    {
        var options = new MsfsAdapterOptions
        {
            InitialRetryDelay = TimeSpan.FromSeconds(initialSeconds),
            MaxRetryDelay = TimeSpan.FromSeconds(maxSeconds),
            ProbeInterval = TimeSpan.FromSeconds(probeSeconds),
            DeadProbesBeforeReconnect = deadProbes,
        };

        Should.Throw<ArgumentOutOfRangeException>(
            () => new MsfsSimAdapter(new FakeSessionFactory(_ => Healthy()), NullLogger<MsfsSimAdapter>.Instance, TimeProvider.System, options));
    }

    [Fact]
    public async Task The_default_options_match_what_the_spike_validated()
    {
        var options = new MsfsAdapterOptions();

        options.InitialRetryDelay.ShouldBe(Seconds(2));
        options.MaxRetryDelay.ShouldBe(Seconds(10));
        options.ProbeInterval.ShouldBe(Seconds(3));
        options.DeadProbesBeforeReconnect.ShouldBe(2);

        // The public constructor needs nothing, and touches SimConnect only when connecting.
        await using var adapter = new MsfsSimAdapter();
        adapter.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
    }
}
