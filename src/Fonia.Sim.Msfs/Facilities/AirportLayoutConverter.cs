using System.Globalization;
using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Sim.Msfs;

/// <summary>The layout made from what the simulator said, and what was left out or guessed on the way (for the person who runs the dump to read).</summary>
/// <param name="Layout">Null when the simulator's data cannot make a layout (no airport or no usable runway, or a graph that does not hold together).</param>
public sealed record LayoutConversion(AirportLayout? Layout, IReadOnlyList<string> Notes);

/// <summary>
/// Makes the app's <see cref="AirportLayout"/> from the raw facility data of MSFS. What the real data showed (docs/decisions/airport-layout.md):
/// the offsets of the taxi points are meters east (X) and north (Z) of the airport reference point; the taxiways are the paths of type 4 (and
/// 1); a runway path (type 2) is the only one whose runway fields mean anything; in a parking path START is the taxi point and END the parking
/// spot; the holding points stand 80 to 130 m from their runway and not next to it in the graph, so each is tied to the nearest runway.
/// </summary>
public static class AirportLayoutConverter
{
    private const double MetersPerDegreeLatitude = 111_320;

    // Parking spots are nodes of their own, numbered apart from the taxi points (which keep the index the simulator gave them).
    private const int ParkingNodeBase = 1_000_000;

    // A holding point farther from every runway than this is not tied to any (the simulator marks them 80 to 130 m out).
    private const double MaxHoldingPointDistanceMeters = 250;

    public static LayoutConversion Convert(AirportFacility facility)
    {
        ArgumentNullException.ThrowIfNull(facility);

        var notes = new List<string>();

        if (facility.Airport is not { } airport)
        {
            return new LayoutConversion(null, ["The simulator did not send the airport."]);
        }

        var frame = new Frame(airport.LatitudeDegrees, airport.LongitudeDegrees);
        var runways = Runways(facility, notes);

        if (runways.Count == 0)
        {
            notes.Add("The airport has no runway the layout can use.");
            return new LayoutConversion(null, notes);
        }

        var nodes = new Dictionary<int, TaxiNode>();

        foreach (var point in facility.TaxiPoints)
        {
            var kind = point.Type is 2 or 4 or 5 or 6 ? TaxiNodeKind.HoldShort : TaxiNodeKind.Normal;

            nodes[point.Index] = new TaxiNode(point.Index, frame.At(point.BiasX, point.BiasZ), kind, kind == TaxiNodeKind.HoldShort ? NearestRunway(runways, frame.At(point.BiasX, point.BiasZ)) : null);
        }

        var tied = nodes.Values.Count(n => n.Kind == TaxiNodeKind.HoldShort && n.Runway is not null);
        var holding = nodes.Values.Count(n => n.Kind == TaxiNodeKind.HoldShort);

        if (tied < holding)
        {
            notes.Add($"{holding - tied} of {holding} holding points are farther than {MaxHoldingPointDistanceMeters:0} m from every runway and are tied to none.");
        }

        var parkingNodes = new Dictionary<int, int>();
        var edges = Edges(facility, runways, nodes, parkingNodes, frame, notes);
        var parkings = Parkings(facility, parkingNodes, frame);
        var layout = new AirportLayout(airport.Icao, runways, [.. nodes.Values.OrderBy(n => n.Id)], edges, parkings, Frequencies(facility, notes));

        try
        {
            AirportLayoutJson.Validate(layout);
        }
        catch (AirportLayoutFormatException ex)
        {
            notes.Add($"The layout does not hold together: {ex.Message}");
            return new LayoutConversion(null, notes);
        }

        return new LayoutConversion(layout, notes);
    }

    // Local meters east/north of the airport reference point to latitude and longitude.
    private sealed record Frame(double LatitudeDegrees, double LongitudeDegrees)
    {
        public GeoPoint At(double east, double north) =>
            new(LatitudeDegrees + (north / MetersPerDegreeLatitude), LongitudeDegrees + (east / (MetersPerDegreeLatitude * Math.Cos(LatitudeDegrees * Math.PI / 180))));
    }

    // The primary end is the one the heading points away from: its threshold is at the start of the run, behind the center.
    private static List<LayoutRunway> Runways(AirportFacility facility, List<string> notes)
    {
        var runways = new List<LayoutRunway>();

        foreach (var raw in facility.Runways)
        {
            if (Designator(raw.PrimaryNumber, raw.PrimaryDesignator) is not { } primary || Designator(raw.SecondaryNumber, raw.SecondaryDesignator) is not { } secondary)
            {
                notes.Add($"A runway ({raw.PrimaryNumber}/{raw.SecondaryNumber}) has a number that is not 1 to 36 and was left out.");
                continue;
            }

            var heading = raw.HeadingDegrees * Math.PI / 180;
            var half = raw.LengthMeters / 2.0;
            var metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(raw.LatitudeDegrees * Math.PI / 180);
            var ahead = new GeoPoint(raw.LatitudeDegrees + (Math.Cos(heading) * half / MetersPerDegreeLatitude), raw.LongitudeDegrees + (Math.Sin(heading) * half / metersPerDegreeLongitude));
            var behind = new GeoPoint(raw.LatitudeDegrees - (Math.Cos(heading) * half / MetersPerDegreeLatitude), raw.LongitudeDegrees - (Math.Sin(heading) * half / metersPerDegreeLongitude));

            var primaryEnd = new LayoutRunwayEnd(primary, behind, Normalize(raw.HeadingDegrees));
            var secondaryEnd = new LayoutRunwayEnd(secondary, ahead, Normalize(raw.HeadingDegrees + 180.0));

            // The end with the lower number first, as the airport data names a runway ("15/33").
            runways.Add(raw.PrimaryNumber <= raw.SecondaryNumber
                ? new LayoutRunway(primaryEnd, secondaryEnd, raw.LengthMeters, raw.WidthMeters)
                : new LayoutRunway(secondaryEnd, primaryEnd, raw.LengthMeters, raw.WidthMeters));
        }

        return runways;
    }

    // The designator of the SDK: number 1 to 36, then L, R, C, W (water), A, B.
    private static string? Designator(int number, int designator)
    {
        if (number is < 1 or > 36)
        {
            return null;
        }

        var suffix = designator switch
        {
            1 => "L",
            2 => "R",
            3 => "C",
            4 => "W",
            5 => "A",
            6 => "B",
            _ => string.Empty,
        };

        return $"{number.ToString("00", CultureInfo.InvariantCulture)}{suffix}";
    }

    private static double Normalize(double degrees) => ((degrees % 360) + 360) % 360;

    private static List<TaxiEdge> Edges(
        AirportFacility facility,
        List<LayoutRunway> runways,
        Dictionary<int, TaxiNode> nodes,
        Dictionary<int, int> parkingNodes,
        Frame frame,
        List<string> notes)
    {
        var edges = new List<TaxiEdge>();
        var parkingsByIndex = facility.Parkings.GroupBy(p => p.Index).ToDictionary(g => g.Key, g => g.First());
        var seen = new HashSet<(int, int, TaxiEdgeKind, string?, string?)>();
        var skipped = new SortedDictionary<int, int>();
        var unnamed = 0;
        var taxiways = 0;
        var strays = 0;

        void Add(TaxiEdge edge)
        {
            if (seen.Add((Math.Min(edge.From, edge.To), Math.Max(edge.From, edge.To), edge.Kind, edge.Name, edge.Runway)))
            {
                edges.Add(edge);
            }
        }

        foreach (var path in facility.TaxiPaths)
        {
            switch (path.Type)
            {
                case 1 or 4:
                    if (!nodes.ContainsKey(path.Start) || !nodes.ContainsKey(path.End) || path.Start == path.End)
                    {
                        strays++;
                        break;
                    }

                    var name = path.NameIndex < facility.TaxiNames.Count && facility.TaxiNames[(int)path.NameIndex].Name.Length > 0 ? facility.TaxiNames[(int)path.NameIndex].Name : null;

                    taxiways++;
                    unnamed += name is null ? 1 : 0;
                    Add(new TaxiEdge(path.Start, path.End, name, TaxiEdgeKind.Taxiway, null));
                    break;

                case 2:
                    var runway = runways.FirstOrDefault(r => RunwayMatches(r, path));

                    if (runway is null || !nodes.ContainsKey(path.Start) || !nodes.ContainsKey(path.End) || path.Start == path.End)
                    {
                        strays++;
                        break;
                    }

                    Add(new TaxiEdge(path.Start, path.End, null, TaxiEdgeKind.Runway, runway.Name));
                    break;

                case 3:
                    // START is the taxi point and END the parking spot.
                    if (!nodes.ContainsKey(path.Start) || !parkingsByIndex.TryGetValue(path.End, out var parking))
                    {
                        strays++;
                        break;
                    }

                    var nodeId = ParkingNodeBase + parking.Index;

                    if (parkingNodes.TryAdd(parking.Index, nodeId))
                    {
                        nodes[nodeId] = new TaxiNode(nodeId, frame.At(parking.BiasX, parking.BiasZ), TaxiNodeKind.Parking);
                    }

                    Add(new TaxiEdge(path.Start, nodeId, null, TaxiEdgeKind.Parking, null));
                    break;

                default:
                    skipped[path.Type] = skipped.GetValueOrDefault(path.Type) + 1;
                    break;
            }
        }

        if (skipped.Count > 0)
        {
            notes.Add($"Paths left out: {string.Join(", ", skipped.Select(s => $"{s.Value} of type {s.Key} ({PathTypeName(s.Key)})"))}.");
        }

        if (strays > 0)
        {
            notes.Add($"{strays} paths refer to a point, a parking spot or a runway the data does not have, and were left out.");
        }

        if (taxiways > 0)
        {
            notes.Add($"{unnamed} of {taxiways} taxiway paths have no name; a route over one of them cannot be spoken.");
        }

        return edges;
    }

    private static bool RunwayMatches(LayoutRunway runway, RawTaxiPath path) =>
        Designator(path.RunwayNumber, path.RunwayDesignator) is { } designator
        && (RunwayDesignator.AreSame(runway.Low.Designator, designator) || RunwayDesignator.AreSame(runway.High.Designator, designator));

    private static string PathTypeName(int type) => type switch
    {
        5 => "closed",
        6 => "vehicle",
        7 => "road",
        8 => "painted line",
        _ => "other",
    };

    // The runway the point is nearest to, if it is near enough to be its holding point.
    private static string? NearestRunway(List<LayoutRunway> runways, GeoPoint point)
    {
        var best = runways
            .Select(r => (Runway: r, Meters: DistanceToRunway(r, point)))
            .OrderBy(x => x.Meters)
            .First();

        return best.Meters <= MaxHoldingPointDistanceMeters ? best.Runway.Name : null;
    }

    // Flat distance in meters from a point to the segment between the thresholds of the runway.
    private static double DistanceToRunway(LayoutRunway runway, GeoPoint point)
    {
        var scaleLongitude = MetersPerDegreeLatitude * Math.Cos(point.LatitudeDegrees * Math.PI / 180);
        var ax = (runway.Low.Position.LongitudeDegrees - point.LongitudeDegrees) * scaleLongitude;
        var ay = (runway.Low.Position.LatitudeDegrees - point.LatitudeDegrees) * MetersPerDegreeLatitude;
        var bx = (runway.High.Position.LongitudeDegrees - point.LongitudeDegrees) * scaleLongitude;
        var by = (runway.High.Position.LatitudeDegrees - point.LatitudeDegrees) * MetersPerDegreeLatitude;
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared == 0 ? 0 : Math.Clamp(-((ax * dx) + (ay * dy)) / lengthSquared, 0, 1);

        return Math.Sqrt(Math.Pow(ax + (t * dx), 2) + Math.Pow(ay + (t * dy), 2));
    }

    private static List<Parking> Parkings(AirportFacility facility, Dictionary<int, int> parkingNodes, Frame frame) =>
        [.. facility.Parkings.Select(p => new Parking(
            ParkingName(p.Name),
            p.Number > 0 ? (int)p.Number : null,
            frame.At(p.BiasX, p.BiasZ),
            parkingNodes.TryGetValue(p.Index, out var node) ? node : null,
            ParkingKindOf(p.Type)))];

    // The SDK's names of a parking spot: 1 to 9 are "parking" (and its compass variants), 10 gate, 11 dock, 12 to 37 gate A to Z.
    private static string ParkingName(int name) => name switch
    {
        >= 1 and <= 9 => "PARKING",
        10 => "GATE",
        11 => "DOCK",
        >= 12 and <= 37 => $"GATE {(char)('A' + (name - 12))}",
        _ => "PARKING",
    };

    private static ParkingKind ParkingKindOf(int type) => type switch
    {
        >= 1 and <= 7 or 14 => ParkingKind.Ramp,
        >= 8 and <= 10 or 15 => ParkingKind.Gate,
        11 => ParkingKind.Dock,
        _ => ParkingKind.Other,
    };

    private static List<AirportFrequency> Frequencies(AirportFacility facility, List<string> notes)
    {
        var frequencies = new List<AirportFrequency>();

        foreach (var f in facility.Frequencies)
        {
            if (f.KiloHertz <= 0)
            {
                notes.Add($"A {f.TypeName} frequency of {f.Hertz} Hz was left out.");
                continue;
            }

            frequencies.Add(new AirportFrequency(FrequencyTypeOf(f.Type), new Frequency(f.KiloHertz), f.Name));
        }

        return frequencies;
    }

    // Approach and departure are both the Controle; clearance delivery is the Tráfego; the advisory frequencies are a Rádio.
    private static AirportFrequencyType FrequencyTypeOf(int type) => type switch
    {
        1 => AirportFrequencyType.Atis,
        2 or 3 or 4 => AirportFrequencyType.Radio,
        5 => AirportFrequencyType.Ground,
        6 => AirportFrequencyType.Tower,
        7 or 14 or 15 => AirportFrequencyType.Delivery,
        8 or 9 => AirportFrequencyType.Approach,
        _ => AirportFrequencyType.Other,
    };
}
