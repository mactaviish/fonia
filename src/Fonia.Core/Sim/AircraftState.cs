namespace Fonia.Core.Sim;

/// <summary>
/// Immutable snapshot of the aircraft as reported by a simulator. Units are part of
/// the property names to avoid unit mix-ups between simulators.
/// </summary>
/// <param name="Timestamp">When the snapshot was taken.</param>
/// <param name="Com1">COM1 frequencies, or <see langword="null"/> when the aircraft has no such radio or it reports no valid frequency.</param>
/// <param name="Com2">Same as <paramref name="Com1"/> for COM2.</param>
/// <param name="TransponderCode">Squawk code as four digits read as decimal (e.g. 1200, 7700).</param>
/// <param name="Registration">Aircraft registration / callsign as set in the simulator (e.g. "PR-ABC").</param>
public sealed record AircraftState(
    DateTimeOffset Timestamp,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double AltitudeMslFeet,
    double AltitudeAglFeet,
    double GroundSpeedKnots,
    double HeadingMagneticDegrees,
    bool OnGround,
    RadioState? Com1,
    RadioState? Com2,
    int TransponderCode,
    string Registration,
    string AircraftType);
