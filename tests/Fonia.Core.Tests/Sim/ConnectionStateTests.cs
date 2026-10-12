using Fonia.Core.Sim;
using Fonia.Sim.Mock;

namespace Fonia.Core.Tests.Sim;

/// <summary>
/// The connection part of the <see cref="ISimAdapter"/> contract: state events, and the difference
/// between losing the connection (adapter keeps trying, streams stay open) and disconnecting on purpose.
/// </summary>
public class ConnectionStateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AircraftState Parked() => MockAircraft.Parked();

    private static List<(SimConnectionState Previous, SimConnectionState Current)> Record(MockSimAdapter sim)
    {
        var changes = new List<(SimConnectionState, SimConnectionState)>();
        sim.ConnectionStateChanged += (_, e) => changes.Add((e.Previous, e.Current));
        return changes;
    }

    private static async Task<AircraftState> NextAsync(IAsyncEnumerator<AircraftState> enumerator)
    {
        (await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct)).ShouldBeTrue("the stream ended early");
        return enumerator.Current;
    }

    [Fact]
    public async Task Connect_goes_through_connecting_to_connected()
    {
        await using var sim = new MockSimAdapter();
        var changes = Record(sim);

        await sim.ConnectAsync(Ct);

        changes.ShouldBe(
        [
            (SimConnectionState.Disconnected, SimConnectionState.Connecting),
            (SimConnectionState.Connecting, SimConnectionState.Connected),
        ]);
    }

    [Fact]
    public async Task Connect_when_already_connected_raises_nothing()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        var changes = Record(sim);

        await sim.ConnectAsync(Ct);

        changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Explicit_disconnect_goes_from_connected_to_disconnected()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        var changes = Record(sim);

        await sim.DisconnectAsync(Ct);
        await sim.DisconnectAsync(Ct);

        changes.ShouldBe([(SimConnectionState.Connected, SimConnectionState.Disconnected)]);
    }

    [Fact]
    public async Task Connection_loss_goes_from_connected_to_connecting_and_keeps_the_last_state()
    {
        await using var sim = new MockSimAdapter();
        sim.SetState(Parked());
        await sim.ConnectAsync(Ct);
        var changes = Record(sim);

        sim.SimulateConnectionLoss();

        sim.ConnectionState.ShouldBe(SimConnectionState.Connecting);
        sim.LatestState.ShouldBe(Parked());
        changes.ShouldBe([(SimConnectionState.Connected, SimConnectionState.Connecting)]);
    }

    [Fact]
    public async Task Restoring_the_connection_goes_from_connecting_to_connected()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        sim.SimulateConnectionLoss();
        var changes = Record(sim);

        sim.RestoreConnection();

        sim.ConnectionState.ShouldBe(SimConnectionState.Connected);
        changes.ShouldBe([(SimConnectionState.Connecting, SimConnectionState.Connected)]);
    }

    [Fact]
    public async Task Streams_stay_open_across_a_connection_loss()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);
        sim.SetState(Parked());
        (await NextAsync(stream)).ShouldBe(Parked());

        sim.SimulateConnectionLoss();
        sim.RestoreConnection();
        sim.SetState(Parked() with { GroundSpeedKnots = 12 });

        (await NextAsync(stream)).GroundSpeedKnots.ShouldBe(12);
    }

    [Fact]
    public async Task State_set_during_a_loss_is_delivered_when_the_connection_returns()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        sim.SetState(Parked());
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);
        sim.SimulateConnectionLoss();

        sim.SetState(Parked() with { GroundSpeedKnots = 30 });

        sim.LatestState.ShouldBe(Parked());

        sim.RestoreConnection();

        (await NextAsync(stream)).GroundSpeedKnots.ShouldBe(30);
        sim.LatestState!.GroundSpeedKnots.ShouldBe(30);
    }

    [Fact]
    public async Task Update_during_a_loss_builds_on_the_latest_programmed_state()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        sim.SetState(Parked());
        sim.SimulateConnectionLoss();
        sim.SetState(Parked() with { GroundSpeedKnots = 30 });

        sim.Update(s => s with { HeadingMagneticDegrees = 200 });
        sim.RestoreConnection();

        sim.LatestState!.GroundSpeedKnots.ShouldBe(30);
        sim.LatestState.HeadingMagneticDegrees.ShouldBe(200);
    }

    [Fact]
    public async Task Explicit_disconnect_during_a_loss_ends_streams_and_goes_to_disconnected()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        await using var stream = sim.StreamAsync(Ct).GetAsyncEnumerator(Ct);
        sim.SimulateConnectionLoss();
        var changes = Record(sim);

        await sim.DisconnectAsync(Ct);

        (await stream.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct)).ShouldBeFalse();
        changes.ShouldBe([(SimConnectionState.Connecting, SimConnectionState.Disconnected)]);
    }

    [Fact]
    public async Task Connect_during_a_loss_changes_nothing_because_the_adapter_is_already_reconnecting()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        sim.SimulateConnectionLoss();
        var changes = Record(sim);

        await sim.ConnectAsync(Ct);

        sim.ConnectionState.ShouldBe(SimConnectionState.Connecting);
        changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dispose_after_a_loss_goes_to_disconnected()
    {
        var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        sim.SimulateConnectionLoss();
        var changes = Record(sim);

        await sim.DisposeAsync();

        changes.ShouldBe([(SimConnectionState.Connecting, SimConnectionState.Disconnected)]);
    }

    [Fact]
    public async Task Simulating_a_loss_requires_a_connection()
    {
        await using var sim = new MockSimAdapter();

        Should.Throw<InvalidOperationException>(sim.SimulateConnectionLoss);

        await sim.ConnectAsync(Ct);
        sim.SimulateConnectionLoss();

        Should.Throw<InvalidOperationException>(sim.SimulateConnectionLoss);
    }

    [Fact]
    public async Task Restoring_requires_a_loss_in_progress()
    {
        await using var sim = new MockSimAdapter();

        Should.Throw<InvalidOperationException>(sim.RestoreConnection);

        await sim.ConnectAsync(Ct);

        Should.Throw<InvalidOperationException>(sim.RestoreConnection);
    }

    [Fact]
    public async Task Com_radio_control_is_unavailable_while_the_connection_is_lost()
    {
        await using var sim = new MockSimAdapter();
        sim.SetState(Parked());
        await sim.ConnectAsync(Ct);
        sim.SimulateConnectionLoss();

        await Should.ThrowAsync<InvalidOperationException>(
            () => sim.SetComFrequencyAsync(ComRadio.Com1, Frequency.FromMhz(118.1), Ct));
    }

    [Fact]
    public async Task State_change_handlers_run_outside_the_adapter_lock()
    {
        // A handler that needs the adapter from another thread must not deadlock.
        await using var sim = new MockSimAdapter();
        var observed = new List<SimConnectionState>();
        sim.ConnectionStateChanged += (_, _) =>
        {
            var read = Task.Run(() => sim.ConnectionState, Ct);
            SpinWait.SpinUntil(() => read.IsCompleted, Timeout).ShouldBeTrue("the adapter lock was held while raising the event");
            observed.Add(read.Result);
        };

        await sim.ConnectAsync(Ct);

        observed.ShouldNotBeEmpty();
    }
}
