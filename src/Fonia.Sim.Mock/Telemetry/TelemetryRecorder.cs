using Fonia.Core.Sim;

namespace Fonia.Sim.Mock.Telemetry;

/// <summary>
/// Records what an <see cref="ISimAdapter"/> publishes to a telemetry file, so a session can be
/// replayed later without the simulator. Works with any adapter, including the real one.
/// </summary>
/// <param name="adapter">The adapter to record. It must already be (or become) connected.</param>
/// <param name="destination">Where to write. Closed when recording ends unless <paramref name="leaveOpen"/> is set.</param>
public sealed class TelemetryRecorder(ISimAdapter adapter, Stream destination, bool leaveOpen)
{
    private int _count;

    /// <summary>Snapshots written so far (header excluded).</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>
    /// Records until <paramref name="ct"/> is cancelled or the adapter ends the stream (disconnect or disposal).
    /// A lost connection does not end the recording. Cancelling completes normally.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        // Subscribe before writing the header: the subscription starts at this call, so nothing published
        // while the header is being written is missed.
        var snapshots = adapter.StreamAsync(ct);

        // Header and lines are written with CancellationToken.None: a cancelled recording must still
        // leave a complete file behind.
        await using var writer = await TelemetryWriter.CreateAsync(destination, leaveOpen, CancellationToken.None);

        await foreach (var snapshot in snapshots)
        {
            await writer.WriteAsync(snapshot, CancellationToken.None);
            Interlocked.Increment(ref _count);
        }
    }
}
