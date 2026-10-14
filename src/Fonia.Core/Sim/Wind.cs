namespace Fonia.Core.Sim;

/// <summary>The surface wind, as the controller reports it: where it blows from, how fast, and the gusts if there are any.</summary>
/// <param name="DirectionDegrees">Where the wind comes from, 0 to 360 (0 and 360 are both north).</param>
/// <param name="SpeedKnots">The mean speed in knots; below one knot the wind is calm.</param>
/// <param name="GustKnots">The speed of the gusts, when they are reported; never below the mean speed.</param>
public sealed record Wind(int DirectionDegrees, int SpeedKnots, int? GustKnots = null)
{
    public int DirectionDegrees { get; } = DirectionDegrees is >= 0 and <= 360
        ? DirectionDegrees
        : throw new ArgumentOutOfRangeException(nameof(DirectionDegrees), DirectionDegrees, "The direction is from 0 to 360 degrees.");

    public int SpeedKnots { get; } = SpeedKnots >= 0
        ? SpeedKnots
        : throw new ArgumentOutOfRangeException(nameof(SpeedKnots), SpeedKnots, "The speed cannot be negative.");

    public int? GustKnots { get; } = GustKnots is null || GustKnots >= SpeedKnots
        ? GustKnots
        : throw new ArgumentOutOfRangeException(nameof(GustKnots), GustKnots, "The gusts cannot be below the mean speed.");
}
