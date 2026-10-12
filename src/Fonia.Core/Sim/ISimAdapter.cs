namespace Fonia.Core.Sim;

/// <summary>
/// The only way the application talks to a simulator. Implementations live in
/// <c>Fonia.Sim.*</c> projects; the core never references simulator-specific APIs.
/// </summary>
/// <remarks>
/// Lifecycle: <see cref="ConnectAsync"/> → states flow → <see cref="DisconnectAsync"/> or
/// <see cref="IAsyncDisposable.DisposeAsync"/>. Disposing is idempotent and implies a disconnect.
/// Adapters that can also write to the simulator expose that through separate capability
/// interfaces such as <see cref="IComRadioControl"/>.
/// </remarks>
public interface ISimAdapter : IAsyncDisposable
{
    /// <summary>Current connection status.</summary>
    SimConnectionState ConnectionState { get; }

    /// <summary>
    /// Last known snapshot, or <see langword="null"/> until the first one arrives after connecting.
    /// After a disconnect it keeps the last snapshot received.
    /// </summary>
    AircraftState? LatestState { get; }

    /// <summary>
    /// Connects to the simulator. Completes when the connection is established and
    /// throws <see cref="OperationCanceledException"/> if <paramref name="ct"/> is cancelled first.
    /// Calling it while already connected is a no-op.
    /// </summary>
    Task ConnectAsync(CancellationToken ct);

    /// <summary>Disconnects and ends all active streams. Calling it while disconnected is a no-op.</summary>
    Task DisconnectAsync(CancellationToken ct);

    /// <summary>
    /// Streams snapshots as they arrive. Each call returns an independent subscription, so several
    /// consumers can read at the same time without taking items from each other. A slow consumer may
    /// miss intermediate snapshots (only the most recent matter) but never blocks the adapter.
    /// The stream ends when <paramref name="ct"/> is cancelled or the adapter disconnects.
    /// </summary>
    IAsyncEnumerable<AircraftState> StreamAsync(CancellationToken ct);
}
