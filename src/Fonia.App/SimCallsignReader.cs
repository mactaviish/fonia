using Fonia.Core.Sim;

namespace Fonia.App;

/// <summary>
/// Reads the aircraft's registration from the simulator: connects, waits for the first state of a loaded flight and
/// disconnects. Used by the ATC console only when the user did not pass --callsign.
/// </summary>
internal sealed class SimCallsignReader(ISimSourceFactory sourceFactory, TimeProvider timeProvider, TimeSpan timeout, Action<string> progress)
{
    /// <summary>The registration ("PR-ABC"), or null if no state came within the time allowed.</summary>
    public async Task<string?> ReadAsync(AppOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var limit = new CancellationTokenSource(timeout, timeProvider);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);

        var source = sourceFactory.Create(options, replay: null);
        Task? drive = null;

        try
        {
            progress($"Lendo o indicativo no simulador ({source.Description})...");

            var first = FirstStateAsync(source.Adapter.StreamAsync(run.Token));

            await source.Adapter.ConnectAsync(run.Token);
            drive = source.Drive?.Invoke(run.Token);

            return (await first)?.Registration;
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            await run.CancelAsync();
            await FinishAsync(drive);
            await DisposeQuietlyAsync(source.Adapter);
        }
    }

    private static async Task<AircraftState?> FirstStateAsync(IAsyncEnumerable<AircraftState> states)
    {
        await foreach (var state in states)
        {
            return state;
        }

        return null;
    }

    private static async Task FinishAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task DisposeQuietlyAsync(ISimAdapter adapter)
    {
        try
        {
            await adapter.DisposeAsync();
        }
        catch (Exception)
        {
            // Closing down: nothing useful to do with a failure here.
        }
    }
}
