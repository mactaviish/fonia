namespace Fonia.Sim.Msfs;

/// <summary>Timing of <see cref="MsfsSimAdapter"/>. The defaults are the values validated against a real MSFS 2020 (see docs/decisions/simconnect.md).</summary>
public sealed record MsfsAdapterOptions
{
    /// <summary>First pause after a failed attempt to reach MSFS. It doubles on each failure up to <see cref="MaxRetryDelay"/>.</summary>
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Longest pause between attempts. The adapter never gives up: MSFS can take minutes to start.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How often a live connection is checked.</summary>
    public TimeSpan ProbeInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>How many probes in a row must find the connection dead before it is rebuilt.</summary>
    public int DeadProbesBeforeReconnect { get; init; } = 2;
}
