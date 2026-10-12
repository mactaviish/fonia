namespace Fonia.Core.Sim;

/// <summary>Connection status between a <see cref="ISimAdapter"/> and its simulator.</summary>
public enum SimConnectionState
{
    Disconnected = 0,
    Connecting,
    Connected,
}
