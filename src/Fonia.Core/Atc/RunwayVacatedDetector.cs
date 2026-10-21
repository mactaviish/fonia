using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Core.Atc;

/// <summary>
/// Tells from the telemetry that an aircraft got clear of a runway: it was on it, and then on the ground far enough from it to have passed the
/// holding point. It is what lets the Torre clear a crossing without the pilot reporting first. Needs the map of the airport (the geometry of the
/// runway); without one it never reports. Pure: the same snapshots always give the same events.
/// </summary>
public sealed class RunwayVacatedDetector
{
    /// <summary>
    /// How far from the runway's centerline the aircraft must be to be clear of it, in meters. The simulator marks the holding points 80 to 130 m
    /// from the runway; this is the near end of that, so that the aircraft is reported when it has passed the nearest of them. Judgement, not data.
    /// </summary>
    public const double ClearOfRunwayMeters = 90;

    // Within this of the edge of the runway the aircraft is on it (the position has some error, and the nose wheel is not the whole aircraft).
    private const double OnRunwayMarginMeters = 10;

    private string? _runway;
    private bool _wasOnRunway;

    /// <param name="layout">The map of the airport, or null.</param>
    /// <param name="runway">The runway to watch, the one the landings use ("10R"), or null.</param>
    /// <returns>The event this snapshot shows, or null. Reported once for each time the aircraft goes from the runway to clear of it.</returns>
    public RunwayVacated? Observe(AircraftState state, AirportLayout? layout, string? runway)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!string.Equals(_runway, runway, StringComparison.Ordinal))
        {
            _runway = runway;
            _wasOnRunway = false;
        }

        if (layout is null || runway is null || !state.OnGround || RunwayOf(layout, runway) is not { } found)
        {
            _wasOnRunway = false;
            return null;
        }

        var meters = MetersFromCenterline(found, state.LatitudeDegrees, state.LongitudeDegrees);

        if (meters <= found.WidthMeters / 2 + OnRunwayMarginMeters)
        {
            _wasOnRunway = true;
            return null;
        }

        if (!_wasOnRunway || meters < ClearOfRunwayMeters)
        {
            return null;
        }

        _wasOnRunway = false;

        return new RunwayVacated(state.Timestamp, new GeoPoint(state.LatitudeDegrees, state.LongitudeDegrees));
    }

    private static LayoutRunway? RunwayOf(AirportLayout layout, string designator) =>
        layout.Runways.FirstOrDefault(r => RunwayDesignator.AreSame(r.Low.Designator, designator) || RunwayDesignator.AreSame(r.High.Designator, designator));

    // The distance from the point to the segment between the two thresholds, on a flat map around the runway (good to the meter at this size).
    private static double MetersFromCenterline(LayoutRunway runway, double latitude, double longitude)
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
