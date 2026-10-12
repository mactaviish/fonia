using Fonia.Core.Sim;
using Fonia.Sim.Mock;

namespace Fonia.Core.Tests.Sim;

public class MockSimAdapterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AircraftState Parked() => MockAircraft.Parked();

    // Bounded waits so a broken stream fails the test instead of hanging it.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static Task<bool> HasNextAsync(IAsyncEnumerator<AircraftState> enumerator)
    {
        return enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct);
    }

    private static async Task<AircraftState> NextAsync(IAsyncEnumerator<AircraftState> enumerator)
    {
        (await HasNextAsync(enumerator)).ShouldBeTrue("the stream ended before a snapshot arrived");
        return enumerator.Current;
    }

    [Fact]
    public void New_adapter_is_disconnected_without_state()
    {
        var sim = new MockSimAdapter();

        sim.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
        sim.LatestState.ShouldBeNull();
    }

    [Fact]
    public async Task Connect_changes_state_and_is_idempotent()
    {
        await using var sim = new MockSimAdapter();

        await sim.ConnectAsync(Ct);
        await sim.ConnectAsync(Ct);

        sim.ConnectionState.ShouldBe(SimConnectionState.Connected);
    }

    [Fact]
    public async Task Connect_with_cancelled_token_throws_and_stays_disconnected()
    {
        await using var sim = new MockSimAdapter();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => sim.ConnectAsync(cts.Token));

        sim.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
    }

    [Fact]
    public async Task State_programmed_before_connect_is_not_visible_until_connected()
    {
        await using var sim = new MockSimAdapter();
        sim.SetState(Parked());

        sim.LatestState.ShouldBeNull();

        await sim.ConnectAsync(Ct);

        sim.LatestState.ShouldBe(Parked());
    }

    [Fact]
    public async Task State_programmed_before_connect_is_the_first_snapshot_on_connect()
    {
        await using var sim = new MockSimAdapter();
        sim.SetState(Parked());
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        await sim.ConnectAsync(Ct);

        (await NextAsync(stream)).ShouldBe(Parked());
    }

    [Fact]
    public async Task SetState_while_connected_updates_latest_and_streams()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        sim.SetState(Parked());

        (await NextAsync(stream)).ShouldBe(Parked());
        sim.LatestState.ShouldBe(Parked());
    }

    [Fact]
    public async Task Update_derives_a_new_snapshot_from_the_current_one()
    {
        await using var sim = new MockSimAdapter();
        sim.SetState(Parked());
        await sim.ConnectAsync(Ct);
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        sim.Update(s => s with { OnGround = false, GroundSpeedKnots = 90 });

        var next = await NextAsync(stream);
        next.OnGround.ShouldBeFalse();
        next.GroundSpeedKnots.ShouldBe(90);
        next.Registration.ShouldBe("PR-ABC");
    }

    [Fact]
    public void Update_without_any_state_throws()
    {
        var sim = new MockSimAdapter();

        Should.Throw<InvalidOperationException>(() => sim.Update(s => s));
    }

    [Fact]
    public async Task Each_stream_is_independent_and_receives_every_snapshot()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        await using var first = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);
        await using var second = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        sim.SetState(Parked());

        (await NextAsync(first)).ShouldBe(Parked());
        (await NextAsync(second)).ShouldBe(Parked());
    }

    [Fact]
    public async Task Slow_consumer_only_sees_the_most_recent_snapshot()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        sim.SetState(Parked() with { GroundSpeedKnots = 1 });
        sim.SetState(Parked() with { GroundSpeedKnots = 2 });
        sim.SetState(Parked() with { GroundSpeedKnots = 3 });

        (await NextAsync(stream)).GroundSpeedKnots.ShouldBe(3);
    }

    [Fact]
    public async Task Cancelling_the_token_ends_the_stream_without_throwing()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        using var cts = new CancellationTokenSource();
        var received = new List<AircraftState>();

        var consumer = Task.Run(
            async () =>
            {
                await foreach (var state in sim.StreamAsync(cts.Token))
                {
                    received.Add(state);
                }
            },
            Ct);

        sim.SetState(Parked());
        await cts.CancelAsync();
        await consumer.WaitAsync(Timeout, Ct);

        consumer.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Disconnect_ends_streams_and_keeps_latest_state()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        sim.SetState(Parked());
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        await sim.DisconnectAsync(Ct);

        (await HasNextAsync(stream)).ShouldBeFalse("the stream should have ended");
        sim.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
        sim.LatestState.ShouldBe(Parked());
    }

    [Fact]
    public async Task Disconnect_while_disconnected_is_a_no_op()
    {
        await using var sim = new MockSimAdapter();

        await sim.DisconnectAsync(Ct);

        sim.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
    }

    [Fact]
    public async Task Reconnecting_delivers_snapshots_to_new_streams()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        await sim.DisconnectAsync(Ct);
        await sim.ConnectAsync(Ct);
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        sim.SetState(Parked());

        (await NextAsync(stream)).ShouldBe(Parked());
    }

    [Fact]
    public async Task Dispose_is_idempotent_and_ends_streams()
    {
        var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        await sim.DisposeAsync();
        await sim.DisposeAsync();

        (await HasNextAsync(stream)).ShouldBeFalse("the stream should have ended");
        sim.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
    }

    [Fact]
    public async Task Using_a_disposed_adapter_throws()
    {
        var sim = new MockSimAdapter();
        await sim.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() => sim.ConnectAsync(Ct));
        Should.Throw<ObjectDisposedException>(() => sim.StreamAsync(Ct));
        Should.Throw<ObjectDisposedException>(() => sim.SetState(Parked()));
    }

    [Fact]
    public async Task SetComFrequency_changes_the_active_frequency_and_publishes()
    {
        await using var sim = new MockSimAdapter();
        sim.SetState(Parked());
        await sim.ConnectAsync(Ct);
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        await sim.SetComFrequencyAsync(ComRadio.Com1, Frequency.FromMhz(118.1), Ct);

        var next = await NextAsync(stream);
        next.Com1.Active.ShouldBe(Frequency.FromMhz(118.1));
        next.Com1.Standby.ShouldBe(Frequency.FromMhz(118.1));
        next.Com2.ShouldBe(Parked().Com2);
    }

    [Fact]
    public async Task SetComFrequency_requires_a_connection()
    {
        await using var sim = new MockSimAdapter();
        sim.SetState(Parked());

        await Should.ThrowAsync<InvalidOperationException>(
            () => sim.SetComFrequencyAsync(ComRadio.Com2, Frequency.FromMhz(118.1), Ct));
    }

    [Fact]
    public void Mock_exposes_the_optional_com_capability()
    {
        ISimAdapter sim = new MockSimAdapter();

        (sim is IComRadioControl).ShouldBeTrue();
    }
}
