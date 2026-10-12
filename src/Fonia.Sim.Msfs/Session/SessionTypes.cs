namespace Fonia.Sim.Msfs;

/// <summary>What <see cref="ISimConnectSession.ProbeAsync"/> found out about the connection.</summary>
internal enum SessionHealth
{
    /// <summary>The simulator answered.</summary>
    Alive,

    /// <summary>
    /// No answer in time. Normal while MSFS sits in a menu or loads a flight (there is no aircraft to ask),
    /// so it does not mean the connection is gone.
    /// </summary>
    NotResponding,

    /// <summary>The connection is gone (MSFS was closed or crashed). The session must be rebuilt.</summary>
    Dead,
}

/// <summary>MSFS cannot be reached right now (not running, or it went away while starting). Retry later.</summary>
internal sealed class SimulatorUnavailableException : Exception
{
    public SimulatorUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Receives what a session reads from the simulator. Called from the library's own threads.</summary>
internal interface ISimConnectSink
{
    /// <summary>The "Sim" system event: a flight is running (<see langword="true"/>) or not (menus, loading).</summary>
    void OnSimRunning(bool running);

    void OnMotion(MotionData data);

    void OnIdentity(IdentityData data);

    void OnRadios(RadioData data);

    void OnTransponder(TransponderData data);

    /// <summary>The session cannot deliver data any more (a subscription failed or was rejected): rebuild it. Raised once.</summary>
    void OnFault(string reason);
}

/// <summary>
/// One live connection to the simulator. A session is single use: after it is disposed, or reports
/// <see cref="SessionHealth.Dead"/>, create a new one from the <see cref="ISimConnectSessionFactory"/>.
/// </summary>
internal interface ISimConnectSession : IAsyncDisposable
{
    /// <summary>
    /// Connects and completes only when the simulator says it is ready (the Open message), which can be
    /// minutes after the process starts.
    /// </summary>
    /// <exception cref="SimulatorUnavailableException">MSFS is not reachable, or went away while it was starting.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    Task OpenAsync(CancellationToken ct);

    /// <summary>Subscribes to the system events and variable groups. Call after <see cref="OpenAsync"/>.</summary>
    Task SubscribeAsync(ISimConnectSink sink, CancellationToken ct);

    /// <summary>Asks the simulator something simple to find out whether the connection still works.</summary>
    Task<SessionHealth> ProbeAsync(CancellationToken ct);
}

internal interface ISimConnectSessionFactory
{
    ISimConnectSession Create();
}
