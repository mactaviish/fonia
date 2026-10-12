namespace Fonia.Core.Sim;

/// <summary>
/// The only way the application talks to a simulator. Implementations live in
/// <c>Fonia.Sim.*</c> projects; the core never references simulator-specific APIs.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle: <see cref="ConnectAsync"/> → snapshots flow → <see cref="DisconnectAsync"/> or
/// <see cref="IAsyncDisposable.DisposeAsync"/>. Disposing is idempotent and implies a disconnect.
/// Adapters that can also write to the simulator expose that through separate capability
/// interfaces such as <see cref="IComRadioControl"/>.
/// </para>
/// <para>
/// A lost connection is not the same as a deliberate disconnect. After <see cref="ConnectAsync"/>
/// the adapter owns the connection: if the simulator goes away (closed, crashed, restarted) the state
/// goes <c>Connected → Connecting</c>, the adapter keeps trying with no limit, and when the simulator is back
/// it goes <c>Connecting → Connected</c>. Streams stay open throughout. Only an explicit
/// <see cref="DisconnectAsync"/>, disposal or cancelling the stream's own token ends them.
/// </para>
/// <para>
/// Adapters publish a snapshot only when the simulator reports valid data (for example, a flight is
/// loaded); consumers never see the placeholder values some simulators report while loading.
/// </para>
/// </remarks>
public interface ISimAdapter : IAsyncDisposable
{
    /// <summary>
    /// Raised after every change of <see cref="ConnectionState"/>, once per change and in order, on an
    /// arbitrary thread and never while the adapter holds an internal lock (handlers may call back into it).
    /// </summary>
    event EventHandler<SimConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>Current connection status.</summary>
    SimConnectionState ConnectionState { get; }

    /// <summary>
    /// Last known snapshot, or <see langword="null"/> until the first one arrives after connecting.
    /// It is kept while the connection is lost and after a disconnect.
    /// </summary>
    AircraftState? LatestState { get; }

    /// <summary>
    /// Starts the connection and completes when it is first established (state <see cref="SimConnectionState.Connected"/>).
    /// Real adapters keep retrying until the simulator is reachable, however long it takes to start, and throw
    /// <see cref="OperationCanceledException"/> (back to <see cref="SimConnectionState.Disconnected"/>) if
    /// <paramref name="ct"/> is cancelled first. It is a no-op when already connected or while the adapter is
    /// re-establishing a lost connection (observe <see cref="ConnectionStateChanged"/> instead).
    /// </summary>
    Task ConnectAsync(CancellationToken ct);

    /// <summary>
    /// Disconnects on purpose, whether connected or reconnecting, and ends all active streams.
    /// Calling it while disconnected is a no-op.
    /// </summary>
    Task DisconnectAsync(CancellationToken ct);

    /// <summary>
    /// Streams snapshots as they arrive. Each call returns an independent subscription, so several
    /// consumers can read at the same time without taking items from each other. A slow consumer may
    /// miss intermediate snapshots (only the most recent matter) but never blocks the adapter.
    /// The stream survives lost connections; it ends when <paramref name="ct"/> is cancelled, on
    /// <see cref="DisconnectAsync"/> or on disposal.
    /// </summary>
    IAsyncEnumerable<AircraftState> StreamAsync(CancellationToken ct);
}
