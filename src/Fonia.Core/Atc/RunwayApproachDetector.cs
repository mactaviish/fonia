using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Core.Atc;

/// <summary>
/// Tells from the telemetry that an aircraft on the ground is close to a runway: to a holding point or to the departure runway itself. The
/// controller who sees it does not wait for the pilot to say "pronto": it gives the next instruction as soon as it can. The event is repeated
/// while the aircraft stays there (<see cref="RepeatEvery"/>), because the unit that answers may change meanwhile (the Solo passes the flight to
/// the Torre) and the engine says nothing twice. Needs the map of the airport; without one it never reports. Pure: the same snapshots give the same events.
/// </summary>
public sealed class RunwayApproachDetector
{
    /// <summary>How close to a holding point the aircraft is said to be close, in meters. Judgement, not data.</summary>
    public const double CloseMeters = 100;

    /// <summary>
    /// How close to the centerline of the departure runway: the holding points of the simulator are 80 to 130 m from it, so an aircraft at the
    /// line of the runway it is going to use is close. Judgement, not data.
    /// </summary>
    public const double CloseToRunwayMeters = 150;

    /// <summary>The time between two events while the aircraft stays close.</summary>
    public static readonly TimeSpan RepeatEvery = TimeSpan.FromSeconds(5);

    private DateTimeOffset? _last;

    /// <param name="layout">The map of the airport, or null.</param>
    /// <param name="runway">The runway the aircraft will depart from ("10L"), or null.</param>
    public NearRunway? Observe(AircraftState state, AirportLayout? layout, string? runway)
    {
        ArgumentNullException.ThrowIfNull(state);

        var position = new GeoPoint(state.LatitudeDegrees, state.LongitudeDegrees);

        if (layout is null || !state.OnGround || !IsClose(position, layout, runway))
        {
            _last = null;
            return null;
        }

        if (_last is { } last && state.Timestamp - last < RepeatEvery)
        {
            return null;
        }

        _last = state.Timestamp;

        return new NearRunway(state.Timestamp, position);
    }

    internal static bool IsClose(GeoPoint position, AirportLayout layout, string? runway) =>
        (runway is not null && RunwayGeometry.RunwayOf(layout, runway) is { } found
            && RunwayGeometry.MetersFromCenterline(found, position.LatitudeDegrees, position.LongitudeDegrees) <= CloseToRunwayMeters)
        || IsNearAHoldingPoint(position, layout);

    internal static bool IsNearAHoldingPoint(GeoPoint position, AirportLayout layout) =>
        layout.Nodes.Any(n => n.Kind == TaxiNodeKind.HoldShort
            && Geo.DistanceKm(position.LatitudeDegrees, position.LongitudeDegrees, n.Position.LatitudeDegrees, n.Position.LongitudeDegrees) * 1000 <= CloseMeters);
}
