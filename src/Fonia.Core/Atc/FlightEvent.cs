using Fonia.Core.Airports;

namespace Fonia.Core.Atc;

/// <summary>Something the telemetry shows happened to the aircraft, which the controller may speak about without the pilot saying anything first.</summary>
public abstract record FlightEvent;

/// <summary>The aircraft left the ground. <paramref name="At"/> is the time of the snapshot that showed it (the controller says only the minutes, in UTC).</summary>
public sealed record TookOff(DateTimeOffset At) : FlightEvent;

/// <summary>The aircraft touched down and stayed on the ground. <paramref name="At"/> is the time of the touchdown (the controller says only the minutes, in UTC).</summary>
public sealed record TouchedDown(DateTimeOffset At) : FlightEvent;

/// <summary>
/// The aircraft that landed is clear of the runway: it left it and is past where an aircraft holds short of it. <paramref name="Position"/> is
/// where it was then, which is where the way to the apron starts (see <see cref="RunwayVacatedDetector"/>).
/// </summary>
public sealed record RunwayVacated(DateTimeOffset At, GeoPoint? Position) : FlightEvent;
