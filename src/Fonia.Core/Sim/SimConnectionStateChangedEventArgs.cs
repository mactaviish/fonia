namespace Fonia.Core.Sim;

/// <summary>
/// Raised by <see cref="ISimAdapter.ConnectionStateChanged"/>. The pair tells what happened:
/// <c>Connected → Connecting</c> is a lost connection that the adapter is re-establishing;
/// <c>Connected → Disconnected</c> is a deliberate disconnect.
/// </summary>
public sealed class SimConnectionStateChangedEventArgs(SimConnectionState previous, SimConnectionState current) : EventArgs
{
    public SimConnectionState Previous { get; } = previous;

    public SimConnectionState Current { get; } = current;
}
