namespace Fonia.Sim.Msfs;

/// <summary>One way the offsets of a taxi point could be laid on the map, and how far the runway points of the airport are from the runways under it.</summary>
public sealed record AxisHypothesis(string Name, double MeanErrorMeters);

/// <summary>The outcome of <see cref="FacilityAxisCheck"/>: the hypotheses, best first.</summary>
public sealed record AxisCheckResult(IReadOnlyList<AxisHypothesis> Ranking, int Points)
{
    public AxisHypothesis Best => Ranking[0];

    /// <summary>True when the best way fits the runways well and the next one does not: the check can be trusted.</summary>
    public bool Convincing => Best.MeanErrorMeters < 40 && Ranking.Count > 1 && Ranking[1].MeanErrorMeters > Best.MeanErrorMeters * 3;
}

/// <summary>
/// The SDK says the taxi points are offsets in meters (BIAS_X, BIAS_Z) from the airport reference point, along the longitudinal and the
/// latitudinal axis, and not which way is positive. The runways have absolute positions, and the graph has paths of the runway type: so each
/// of the eight ways to lay the offsets on the map is tried, and the one that puts those points on the runways is the convention.
/// </summary>
public static class FacilityAxisCheck
{
    private const double MetersPerDegreeLatitude = 111_320;

    private static readonly (string Name, Func<float, float, (double East, double North)> Map)[] Hypotheses =
    [
        ("X east, Z north", (x, z) => (x, z)),
        ("X east, Z south", (x, z) => (x, -z)),
        ("X west, Z north", (x, z) => (-x, z)),
        ("X west, Z south", (x, z) => (-x, -z)),
        ("X north, Z east", (x, z) => (z, x)),
        ("X north, Z west", (x, z) => (-z, x)),
        ("X south, Z east", (x, z) => (z, -x)),
        ("X south, Z west", (x, z) => (-z, -x)),
    ];

    /// <returns>Null when there is nothing to check with: no airport, no runway, or fewer than two points on runway paths.</returns>
    public static AxisCheckResult? Evaluate(AirportFacility facility)
    {
        ArgumentNullException.ThrowIfNull(facility);

        if (facility.Airport is not { } airport || facility.Runways.Count == 0)
        {
            return null;
        }

        var scaleLongitude = MetersPerDegreeLatitude * Math.Cos(airport.LatitudeDegrees * Math.PI / 180);
        var runways = facility.Runways.Select(r => Segment(r, airport, scaleLongitude)).ToList();

        var indexes = facility.TaxiPaths
            .Where(p => p.Type == 2)
            .SelectMany(p => new[] { p.Start, p.End })
            .Distinct()
            .ToHashSet();
        var points = facility.TaxiPoints.Where(p => indexes.Contains(p.Index)).ToList();

        if (points.Count < 2)
        {
            return null;
        }

        var ranking = Hypotheses
            .Select(h => new AxisHypothesis(h.Name, points.Average(p =>
            {
                var (east, north) = h.Map(p.BiasX, p.BiasZ);

                return runways.Min(s => DistanceToSegment(east, north, s));
            })))
            .OrderBy(h => h.MeanErrorMeters)
            .ToList();

        return new AxisCheckResult(ranking, points.Count);
    }

    // A runway as the segment between its ends, in meters east and north of the airport reference point.
    private static (double EastA, double NorthA, double EastB, double NorthB) Segment(RawRunway runway, RawAirport airport, double scaleLongitude)
    {
        var east = (runway.LongitudeDegrees - airport.LongitudeDegrees) * scaleLongitude;
        var north = (runway.LatitudeDegrees - airport.LatitudeDegrees) * MetersPerDegreeLatitude;
        var heading = runway.HeadingDegrees * Math.PI / 180;
        var half = runway.LengthMeters / 2.0;

        return (east + (Math.Sin(heading) * half), north + (Math.Cos(heading) * half), east - (Math.Sin(heading) * half), north - (Math.Cos(heading) * half));
    }

    private static double DistanceToSegment(double east, double north, (double EastA, double NorthA, double EastB, double NorthB) s)
    {
        var dx = s.EastB - s.EastA;
        var dy = s.NorthB - s.NorthA;
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared == 0 ? 0 : Math.Clamp((((east - s.EastA) * dx) + ((north - s.NorthA) * dy)) / lengthSquared, 0, 1);

        return Math.Sqrt(Math.Pow(east - (s.EastA + (t * dx)), 2) + Math.Pow(north - (s.NorthA + (t * dy)), 2));
    }
}
