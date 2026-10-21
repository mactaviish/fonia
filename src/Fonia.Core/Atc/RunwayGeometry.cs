using Fonia.Core.Airports;

namespace Fonia.Core.Atc;

// The geometry of a runway on a flat map around it (good to the meter at this size), shared by the detectors and the engine.
internal static class RunwayGeometry
{
    public static LayoutRunway? RunwayOf(AirportLayout layout, string designator) =>
        layout.Runways.FirstOrDefault(r => RunwayDesignator.AreSame(r.Low.Designator, designator) || RunwayDesignator.AreSame(r.High.Designator, designator));

    // The distance from the point to the segment between the two thresholds.
    public static double MetersFromCenterline(LayoutRunway runway, double latitude, double longitude)
    {
        var origin = runway.Low.Position;
        var metersPerDegreeLongitude = 111_320 * Math.Cos(origin.LatitudeDegrees * Math.PI / 180);

        var (endX, endY) = ((runway.High.Position.LongitudeDegrees - origin.LongitudeDegrees) * metersPerDegreeLongitude, (runway.High.Position.LatitudeDegrees - origin.LatitudeDegrees) * 110_540);
        var (x, y) = ((longitude - origin.LongitudeDegrees) * metersPerDegreeLongitude, (latitude - origin.LatitudeDegrees) * 110_540);

        var length = endX * endX + endY * endY;
        var along = length == 0 ? 0 : Math.Clamp((x * endX + y * endY) / length, 0, 1);

        return Math.Sqrt(Math.Pow(x - along * endX, 2) + Math.Pow(y - along * endY, 2));
    }
}
