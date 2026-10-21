using Fonia.Core.Sim;

namespace Fonia.Core.Airports;

/// <summary>A place of the airport map where the practice puts the aircraft, with how to call it.</summary>
public sealed record PracticePlace(GeoPoint Position, string Description);

/// <summary>
/// Places of the stored map of an airport where an aircraft can be put without a simulator, to practice the taxi route and the runway
/// crossings by text: a parking position, the holding point of a runway, the threshold of a runway.
/// </summary>
public static class PracticePlaces
{
    /// <summary>The parking position with this number (the first one, when the groups repeat it), or null.</summary>
    public static PracticePlace? Parking(AirportLayout layout, int number)
    {
        var parking = layout.Parkings.FirstOrDefault(p => p.Number == number);

        return parking is null ? null : new PracticePlace(parking.Position, $"{parking.Name} (vaga {number})");
    }

    /// <summary>The holding point nearest to this end of a runway ("15", "10L"), or null when the map has no such end or no holding point for it.</summary>
    public static PracticePlace? HoldingPoint(AirportLayout layout, string designator)
    {
        if (FindEnd(layout, designator) is not { } found)
        {
            return null;
        }

        var (runway, end) = found;
        var nearest = layout.Nodes
            .Where(n => n.Kind == TaxiNodeKind.HoldShort && runway.Matches(n.Runway))
            .OrderBy(n => Meters(n.Position, end.Position))
            .FirstOrDefault();

        return nearest is null ? null : new PracticePlace(nearest.Position, $"ponto de espera da pista {end.Designator}");
    }

    /// <summary>The threshold of this end of a runway, or null when the map has no such end.</summary>
    public static PracticePlace? Threshold(AirportLayout layout, string designator) =>
        FindEnd(layout, designator) is { } found ? new PracticePlace(found.End.Position, $"cabeceira da pista {found.End.Designator}") : null;

    private static (LayoutRunway Runway, LayoutRunwayEnd End)? FindEnd(AirportLayout layout, string designator)
    {
        foreach (var runway in layout.Runways)
        {
            if (RunwayDesignator.AreSame(runway.Low.Designator, designator))
            {
                return (runway, runway.Low);
            }

            if (RunwayDesignator.AreSame(runway.High.Designator, designator))
            {
                return (runway, runway.High);
            }
        }

        return null;
    }

    private static double Meters(GeoPoint a, GeoPoint b) =>
        Geo.DistanceKm(a.LatitudeDegrees, a.LongitudeDegrees, b.LatitudeDegrees, b.LongitudeDegrees) * 1000;
}
