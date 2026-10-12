namespace Fonia.Core.Sim;

/// <summary>Connection status between a <see cref="ISimAdapter"/> and its simulator.</summary>
public enum SimConnectionState
{
    /// <summary>Not started, disconnected on purpose, or disposed. The adapter does nothing on its own.</summary>
    Disconnected = 0,

    /// <summary>
    /// Trying to reach the simulator: the first connection after <see cref="ISimAdapter.ConnectAsync"/>,
    /// or re-establishing a connection that was lost. The adapter keeps trying by itself.
    /// </summary>
    Connecting,

    /// <summary>The simulator is reachable and snapshots can arrive.</summary>
    Connected,
}
