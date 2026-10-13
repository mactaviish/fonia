using Fonia.Core.Sim;

namespace Fonia.Sim.Msfs.Tests;

/// <summary>
/// The whole <see cref="MsfsSimAdapter"/> against a running MSFS 2020 with a flight loaded: read-only, skipped
/// unless <c>FONIA_LIVE_MSFS=1</c> (see <see cref="LiveMsfsSessionTests"/>). Closing and reopening MSFS is not
/// automated here; that scenario is checked by hand.
/// </summary>
[Trait("Category", "Live")]
public class LiveMsfsAdapterTests
{
    private const string SkipReason = "Needs a running MSFS 2020 with a flight loaded. Set FONIA_LIVE_MSFS=1 to run.";
    private static readonly TimeSpan FlightLoadTimeout = TimeSpan.FromSeconds(90);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<AircraftState> FirstStateAsync(MsfsSimAdapter adapter)
    {
        await using var stream = adapter.StreamAsync(Ct).GetAsyncEnumerator(Ct);
        var next = stream.MoveNextAsync().AsTask();

        if (await Task.WhenAny(next, Task.Delay(FlightLoadTimeout, Ct)) != next)
        {
            Assert.Skip($"No valid snapshot within {FlightLoadTimeout.TotalSeconds:F0} s: is a flight loaded in MSFS (not just the main menu)?");
        }

        (await next).ShouldBeTrue();
        return stream.Current;
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(LiveMsfsSessionTests.LiveEnabled), SkipType = typeof(LiveMsfsSessionTests))]
    public async Task The_adapter_connects_streams_real_data_and_disconnects_cleanly()
    {
        await using var adapter = new MsfsSimAdapter();
        var changes = new List<(SimConnectionState, SimConnectionState)>();
        adapter.ConnectionStateChanged += (_, e) => changes.Add((e.Previous, e.Current));
        await using var endStream = adapter.StreamAsync(Ct).GetAsyncEnumerator(Ct);

        await adapter.ConnectAsync(Ct).WaitAsync(TimeSpan.FromMinutes(2), Ct);
        var state = await FirstStateAsync(adapter);

        adapter.ConnectionState.ShouldBe(SimConnectionState.Connected);
        adapter.LatestState.ShouldNotBeNull();
        (Math.Abs(state.LatitudeDegrees) >= 0.05 || Math.Abs(state.LongitudeDegrees) >= 0.05).ShouldBeTrue("the loading placeholder position was published");
        state.Registration.ShouldNotBeNullOrWhiteSpace();
        state.AircraftType.ShouldNotBeNullOrWhiteSpace();
        state.Com1.ShouldNotBeNull();

        var lastBeforeDisconnect = adapter.LatestState;

        await adapter.DisconnectAsync(Ct);

        adapter.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
        adapter.LatestState.ShouldBe(lastBeforeDisconnect); // kept after disconnecting

        // The stream hands over what was already buffered, then ends.
        while (await endStream.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), Ct))
        {
        }

        changes.ShouldBe(
        [
            (SimConnectionState.Disconnected, SimConnectionState.Connecting),
            (SimConnectionState.Connecting, SimConnectionState.Connected),
            (SimConnectionState.Connected, SimConnectionState.Disconnected),
        ]);
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(LiveMsfsSessionTests.LiveEnabled), SkipType = typeof(LiveMsfsSessionTests))]
    public async Task The_adapter_can_connect_again_after_disconnecting()
    {
        await using var adapter = new MsfsSimAdapter();

        await adapter.ConnectAsync(Ct).WaitAsync(TimeSpan.FromMinutes(2), Ct);
        await FirstStateAsync(adapter);
        await adapter.DisconnectAsync(Ct);

        await adapter.ConnectAsync(Ct).WaitAsync(TimeSpan.FromMinutes(2), Ct);
        var state = await FirstStateAsync(adapter);

        adapter.ConnectionState.ShouldBe(SimConnectionState.Connected);
        state.Registration.ShouldNotBeNullOrWhiteSpace();
    }
}
