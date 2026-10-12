using Fonia.Core.Sim;
using Fonia.Sim.Mock;
using Fonia.Sim.Mock.Telemetry;

namespace Fonia.Core.Tests.Telemetry;

public class TelemetryRecorderTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task WaitForCountAsync(TelemetryRecorder recorder, int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(Timeout);

        while (recorder.Count < count)
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private static async Task<List<AircraftState>> ReadBackAsync(MemoryStream stream)
    {
        stream.Position = 0;
        var states = new List<AircraftState>();
        await foreach (var state in TelemetryReader.ReadAsync(stream, Ct))
        {
            states.Add(state);
        }

        return states;
    }

    [Fact]
    public async Task Records_every_snapshot_the_adapter_publishes_in_order()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        var file = new MemoryStream();
        var recorder = new TelemetryRecorder(sim, file, leaveOpen: true);
        using var stop = new CancellationTokenSource();
        var running = recorder.RunAsync(stop.Token);
        var script = MockAircraft.TaxiSequence(4);

        // The stream keeps only the latest snapshot for a slow consumer, so pace the script.
        for (var i = 0; i < script.Count; i++)
        {
            sim.SetState(script[i]);
            await WaitForCountAsync(recorder, i + 1);
        }

        await stop.CancelAsync();
        await running.WaitAsync(Timeout, Ct);

        recorder.Count.ShouldBe(4);
        (await ReadBackAsync(file)).ShouldBe(script);
    }

    [Fact]
    public async Task Cancelling_ends_the_recording_without_throwing_and_leaves_a_valid_file()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        var file = new MemoryStream();
        var recorder = new TelemetryRecorder(sim, file, leaveOpen: true);
        using var stop = new CancellationTokenSource();
        var running = recorder.RunAsync(stop.Token);

        await stop.CancelAsync();
        await running.WaitAsync(Timeout, Ct);

        running.IsCompletedSuccessfully.ShouldBeTrue();
        (await ReadBackAsync(file)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Recording_ends_when_the_adapter_disconnects()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        var file = new MemoryStream();
        var recorder = new TelemetryRecorder(sim, file, leaveOpen: true);
        var running = recorder.RunAsync(Ct);
        sim.SetState(MockAircraft.Parked());
        await WaitForCountAsync(recorder, 1);

        await sim.DisconnectAsync(Ct);
        await running.WaitAsync(Timeout, Ct);

        (await ReadBackAsync(file)).ShouldBe([MockAircraft.Parked()]);
    }

    [Fact]
    public async Task Recording_survives_a_connection_loss()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        var file = new MemoryStream();
        var recorder = new TelemetryRecorder(sim, file, leaveOpen: true);
        using var stop = new CancellationTokenSource();
        var running = recorder.RunAsync(stop.Token);
        sim.SetState(MockAircraft.Parked());
        await WaitForCountAsync(recorder, 1);

        sim.SimulateConnectionLoss();
        sim.RestoreConnection();
        await WaitForCountAsync(recorder, 2); // the mock redelivers the latest state when the link returns
        sim.SetState(MockAircraft.Parked() with { GroundSpeedKnots = 15 });
        await WaitForCountAsync(recorder, 3);
        await stop.CancelAsync();
        await running.WaitAsync(Timeout, Ct);

        var read = await ReadBackAsync(file);
        read[0].ShouldBe(MockAircraft.Parked());
        read[^1].GroundSpeedKnots.ShouldBe(15);
    }

    [Fact]
    public async Task The_header_is_written_before_any_snapshot_arrives()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);
        var file = new MemoryStream();
        var recorder = new TelemetryRecorder(sim, file, leaveOpen: true);
        using var stop = new CancellationTokenSource();

        var running = recorder.RunAsync(stop.Token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(Timeout);
        while (file.Length == 0)
        {
            await Task.Delay(5, timeout.Token);
        }

        await stop.CancelAsync();
        await running.WaitAsync(Timeout, Ct);

        file.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_stream_is_closed_only_when_asked()
    {
        await using var sim = new MockSimAdapter();
        await sim.ConnectAsync(Ct);

        var kept = new MemoryStream();
        await new TelemetryRecorder(sim, kept, leaveOpen: true).RunAsync(new CancellationToken(canceled: true));
        kept.CanWrite.ShouldBeTrue();

        var closed = new MemoryStream();
        await new TelemetryRecorder(sim, closed, leaveOpen: false).RunAsync(new CancellationToken(canceled: true));
        closed.CanWrite.ShouldBeFalse();
    }
}
