namespace Fonia.Core.Airports;

/// <summary>A position on the Earth, in degrees.</summary>
public sealed record GeoPoint(double LatitudeDegrees, double LongitudeDegrees);

/// <summary>One end of a runway as the layout has it: where the threshold is and where the runway points.</summary>
/// <param name="Designator">"15", "33R": the same designators as <see cref="RunwayEnd"/>, so the two can be matched with <see cref="RunwayDesignator"/>.</param>
public sealed record LayoutRunwayEnd(string Designator, GeoPoint Position, double TrueHeadingDegrees);

/// <summary>A runway with its geometry. Lengths in meters, as the simulator gives them (the airport data of <see cref="Runway"/> is in feet).</summary>
public sealed record LayoutRunway(LayoutRunwayEnd Low, LayoutRunwayEnd High, double LengthMeters, double WidthMeters)
{
    /// <summary>"15/33".</summary>
    public string Name => $"{Low.Designator}/{High.Designator}";

    /// <summary>Whether <paramref name="name"/> ("15/33" or "33/15", written as the airport data writes the designators) is this runway.</summary>
    public bool Matches(string? name) => SameName(Name, name);

    /// <summary>Whether two names of a runway, "15/33", are the same runway: the same two designators, in whichever order.</summary>
    public static bool SameName(string? a, string? b)
    {
        var first = a?.Split('/');
        var second = b?.Split('/');

        return first is { Length: 2 } && second is { Length: 2 }
            && ((RunwayDesignator.AreSame(first[0], second[0]) && RunwayDesignator.AreSame(first[1], second[1]))
                || (RunwayDesignator.AreSame(first[0], second[1]) && RunwayDesignator.AreSame(first[1], second[0])));
    }
}

public enum TaxiNodeKind
{
    /// <summary>A junction or a bend.</summary>
    Normal,

    /// <summary>A holding point: where the aircraft stops short of a runway.</summary>
    HoldShort,

    /// <summary>Where a parking position enters the graph.</summary>
    Parking,
}

/// <summary>
/// A point of the taxi graph. <paramref name="Id"/> is unique in the layout. <paramref name="Runway"/> is, for a holding point, the runway
/// ("11/29") it holds short of; the simulator does not say it, so it is the runway nearest to the point. Null for the other nodes, and for a
/// holding point that is not near any runway.
/// </summary>
public sealed record TaxiNode(int Id, GeoPoint Position, TaxiNodeKind Kind, string? Runway = null);

public enum TaxiEdgeKind
{
    Taxiway,

    /// <summary>A stretch of runway: a route that follows it, or crosses it, is on the runway.</summary>
    Runway,

    /// <summary>The way from a parking position into the taxiways.</summary>
    Parking,
}

/// <summary>
/// A stretch of the taxi graph between two nodes. <paramref name="Name"/> is the taxiway's ("A", "B2"), null when the simulator gives none
/// (and always for a runway); <paramref name="Runway"/> is the "15/33" a <see cref="TaxiEdgeKind.Runway"/> edge belongs to, null otherwise.
/// </summary>
public sealed record TaxiEdge(int From, int To, string? Name, TaxiEdgeKind Kind, string? Runway);

public enum ParkingKind
{
    Gate,
    Ramp,
    Dock,
    Other,
}

/// <summary>A place to park, with the node where it enters the taxi graph (null when it is not connected).</summary>
/// <param name="Name">"PATIO", "GATE", "RAMP": what the simulator calls the group.</param>
/// <param name="Number">The number of the position, when it has one.</param>
public sealed record Parking(string Name, int? Number, GeoPoint Position, int? Node, ParkingKind Kind);

/// <summary>
/// What the simulator knows about the inside of an airport, kept only on the user's computer (<c>docs/decisions/airport-layout.md</c>):
/// the runways with their geometry, the taxi graph, the parking positions and the radio frequencies. Without it the app says the runway in
/// use and nothing more; with it, and only when it is solid, it can say the taxiways and the runway crossings. The frequencies are the
/// simulator's, to be weighed against those of the airport data before they are used.
/// </summary>
/// <param name="Ident">The code of the airport, as in <see cref="Airport.Ident"/>.</param>
public sealed record AirportLayout(
    string Ident,
    IReadOnlyList<LayoutRunway> Runways,
    IReadOnlyList<TaxiNode> Nodes,
    IReadOnlyList<TaxiEdge> Edges,
    IReadOnlyList<Parking> Parkings,
    IReadOnlyList<AirportFrequency> Frequencies)
{
    /// <summary>
    /// Whether there is any taxiway to speak of. A scenery without them (the runways and nothing else) is a valid layout, and then the
    /// controller says what it says today.
    /// </summary>
    public bool HasTaxiways => Edges.Any(e => e.Kind == TaxiEdgeKind.Taxiway);
}

/// <summary>The text is not a layout the app can trust: wrong format, a version it does not know, or a graph that does not hold together.</summary>
public sealed class AirportLayoutFormatException : Exception
{
    public AirportLayoutFormatException(string message)
        : base(message)
    {
    }

    public AirportLayoutFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
