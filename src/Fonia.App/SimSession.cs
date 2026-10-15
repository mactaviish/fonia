using Fonia.Core.Sim;

namespace Fonia.App;

/// <summary>
/// The connection of the ATC console to the simulator, kept open for the whole session: it reads the aircraft's registration
/// (when the user did not pass --callsign) and then keeps following the aircraft, so that the radio the pilot has tuned can be
/// read at every transmission.
/// </summary>
internal sealed class SimSession(ISimSourceFactory sourceFactory, TimeProvider timeProvider, TimeSpan timeout, Action<string> progress) : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private SimSource? _source;
    private Task? _drive;

    /// <summary>The frequency COM1 is on now, or null before the first state of a loaded flight (or without a radio).</summary>
    public Frequency? Com1 => _source?.Adapter.LatestState?.Com1?.Active;

    /// <summary>The snapshots of the aircraft from now on, or null when there is no connection (the callsign was given, so none was opened).</summary>
    public IAsyncEnumerable<AircraftState>? States(CancellationToken ct) => _source?.Adapter.StreamAsync(ct);

    /// <summary>
    /// Connects, waits for the first state of a loaded flight and returns the registration ("PR-ABC"), keeping the connection
    /// open. Returns null if no state came within the time allowed.
    /// </summary>
    public async Task<string?> ReadCallsignAsync(AppOptions options, CancellationToken ct) =>
        (await ReadFirstStateAsync(options, timeout, ct))?.Registration;

    /// <summary>
    /// Connects and waits up to <paramref name="wait"/> for the first state of a loaded flight, keeping the connection open: the
    /// registration and the position are what the start-up needs. Returns null if no state came in time (no simulator, or no flight yet).
    /// </summary>
    public async Task<AircraftState?> ReadFirstStateAsync(AppOptions options, TimeSpan wait, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var limit = new CancellationTokenSource(wait, timeProvider);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token, _lifetime.Token);

        var source = sourceFactory.Create(options, replay: null);
        _source = source;

        try
        {
            progress($"Procurando o simulador ({source.Description})...");

            var first = FirstStateAsync(source.Adapter.StreamAsync(run.Token));

            await source.Adapter.ConnectAsync(run.Token);

            // The work that feeds a mock or a replay lives as long as the session, not as long as this reading.
            _drive = source.Drive?.Invoke(_lifetime.Token);

            return await first;
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            await run.CancelAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        await FinishAsync(_drive);

        if (_source is not null)
        {
            await DisposeQuietlyAsync(_source.Adapter);
        }

        _lifetime.Dispose();
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
