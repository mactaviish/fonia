namespace Fonia.Core.Atc;

/// <summary>Something the telemetry shows happened to the aircraft, which the controller may speak about without the pilot saying anything first.</summary>
public abstract record FlightEvent;

/// <summary>The aircraft left the ground. <paramref name="At"/> is the time of the snapshot that showed it (the controller says only the minutes, in UTC).</summary>
public sealed record TookOff(DateTimeOffset At) : FlightEvent;
