namespace Fonia.Sim.Msfs;

// Raw values exactly as SimConnect reports them, in the units chosen after the spike
// (see docs/decisions/simconnect.md). Converting them to Core types is MsfsConversions' job.

/// <summary>Position and motion (SimVars PLANE LATITUDE, PLANE LONGITUDE, PLANE ALTITUDE, PLANE ALT ABOVE GROUND, GROUND VELOCITY, PLANE HEADING DEGREES MAGNETIC, SIM ON GROUND).</summary>
/// <param name="SimOnGround">SIM ON GROUND as the integer SimConnect returns (0 or 1).</param>
internal readonly record struct MotionData(
    double LatitudeDegrees,
    double LongitudeDegrees,
    double AltitudeMslFeet,
    double AltitudeAglFeet,
    double GroundSpeedKnots,
    double HeadingMagneticDegrees,
    int SimOnGround);

/// <summary>ATC ID (registration), ATC TYPE (manufacturer), ATC MODEL (type code or a translation key) and TITLE.</summary>
internal sealed record IdentityData(string AtcId, string AtcType, string AtcModel, string Title);

/// <summary>COM ACTIVE/STANDBY FREQUENCY:1 and :2, read in Hz (the MHz unit returned wrong COM1 values through the library's struct path).</summary>
internal readonly record struct RadioData(
    double Com1ActiveHz,
    double Com1StandbyHz,
    double Com2ActiveHz,
    double Com2StandbyHz);

/// <summary>TRANSPONDER CODE:1 in unit bco16: four BCD digits, so squawk 1234 is 0x1234.</summary>
internal readonly record struct TransponderData(int CodeBco16);
