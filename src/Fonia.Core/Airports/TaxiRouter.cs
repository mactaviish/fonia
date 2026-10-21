using Fonia.Core.Sim;

namespace Fonia.Core.Airports;

/// <summary>A runway the route goes over: crossed, or followed for a stretch.</summary>
/// <param name="Runway">The runway as the layout's edges name it ("11/29").</param>
/// <param name="NodeId">The node where the route reaches the runway.</param>
/// <param name="HoldShortNodeId">
/// The holding point before the runway, where the aircraft stops until it is cleared to cross; null when the graph does not mark the node
/// before the runway as one (then the crossing is there, and the place to stop is not known).
/// </param>
/// <param name="Along">True when the route follows the runway for some stretch (taxiing on it), false when it only crosses it.</param>
public sealed record RunwayCrossing(string Runway, int NodeId, int? HoldShortNodeId, bool Along);

/// <summary>A route over the taxi graph, from where the aircraft is to the holding point of a runway.</summary>
/// <param name="NodeIds">The nodes of the route, in order, from the one the aircraft is at to the holding point.</param>
/// <param name="Taxiways">The names of the taxiways in the order they are used, each once in a row ("A", "B").</param>
/// <param name="Crossings">The runways the route goes over, in order.</param>
/// <param name="HoldingPointNodeId">The node of the holding point of the runway, where the route ends.</param>
public sealed record TaxiRoute(
    IReadOnlyList<int> NodeIds,
    IReadOnlyList<string> Taxiways,
    IReadOnlyList<RunwayCrossing> Crossings,
    int HoldingPointNodeId,
    double LengthMeters);

/// <summary>Why no route can be spoken. In every case the controller says what it says without a layout.</summary>
public enum NoRouteReason
{
    /// <summary>The layout has no taxiway at all (a scenery with the runways and nothing else).</summary>
    NoTaxiways,

    /// <summary>The runway is not one of the layout's.</summary>
    UnknownRunway,

    /// <summary>The aircraft is too far from every node to say where it is on the graph.</summary>
    AircraftOffTheGraph,

    /// <summary>The graph marks no holding point at that runway.</summary>
    NoHoldingPoint,

    /// <summary>The graph is cut: there is no way from the aircraft to the holding point.</summary>
    NotConnected,

    /// <summary>Too much of the best way is over taxiways that have no name, which cannot be said (see <see cref="TaxiRouter.MaxUnnamedShare"/>).</summary>
    UnnamedTaxiway,
}

public abstract record TaxiRouteResult;

public sealed record TaxiRouteFound(TaxiRoute Route) : TaxiRouteResult;

public sealed record NoTaxiRoute(NoRouteReason Reason) : TaxiRouteResult;

/// <summary>
/// The shortest way over the taxi graph from where the aircraft is to the holding point of a runway, with the taxiways to say and the
/// runways it goes over. Pure logic over an <see cref="AirportLayout"/>. It never makes a route up: a graph that is cut, a runway without a
/// holding point, or a way that needs an unnamed taxiway give <see cref="NoTaxiRoute"/>, and the caller falls back to the plain instruction.
/// </summary>
public static class TaxiRouter
{
    /// <summary>The aircraft farther than this from every node is not taken to be on the graph.</summary>
    public const double MaxDistanceToGraphMeters = 150;

    /// <summary>
    /// The most of the taxiway length of a route that may be unnamed. The simulator names about 70% of the taxiways of a big airport and
    /// about a quarter of those of another; the unnamed stretches are mostly the short connectors of an intersection and the taxilanes of the
    /// apron, which the controller does not name. The route says only the taxiways that have a name, and past this share it is not said.
    /// </summary>
    public const double MaxUnnamedShare = 0.4;

    /// <summary>
    /// The longest run of unnamed taxiway, in meters, that a route may have: the apron before the first named taxiway is of this order, but a
    /// pilot cannot be left to find alone a longer one.
    /// </summary>
    public const double MaxUnnamedRunMeters = 300;

    // Going on a runway as if it were a taxiway, or on a taxiway that cannot be named, is allowed but costs as much as a long detour:
    // a way around is always preferred to one the controller could not say.
    private const double RunwayEdgePenaltyMeters = 5000;
    private const double UnnamedTaxiwayPenaltyMeters = 3000;

    public static TaxiRouteResult Find(AirportLayout layout, GeoPoint from, string runway)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(runway);

        if (!layout.HasTaxiways)
        {
            return new NoTaxiRoute(NoRouteReason.NoTaxiways);
        }

        var target = layout.Runways
            .Select(r => (Runway: r, End: new[] { r.Low, r.High }.FirstOrDefault(e => RunwayDesignator.AreSame(e.Designator, runway))))
            .FirstOrDefault(x => x.End is not null);

        if (target.End is null)
        {
            return new NoTaxiRoute(NoRouteReason.UnknownRunway);
        }

        var nodes = layout.Nodes.ToDictionary(n => n.Id);

        var start = layout.Nodes
            .Select(n => (Node: n, Meters: MetersBetween(from, n.Position)))
            .OrderBy(x => x.Meters)
            .FirstOrDefault();

        if (start.Node is null || start.Meters > MaxDistanceToGraphMeters)
        {
            return new NoTaxiRoute(NoRouteReason.AircraftOffTheGraph);
        }

        var runwaysAt = RunwaysAtNodes(layout);
        var holdingPoint = HoldingPointOf(layout, target.Runway, target.End);

        if (holdingPoint is null)
        {
            return new NoTaxiRoute(NoRouteReason.NoHoldingPoint);
        }

        if (ShortestPath(layout, nodes, start.Node.Id, holdingPoint.Id) is not { } path)
        {
            return new NoTaxiRoute(NoRouteReason.NotConnected);
        }

        var used = path.EdgeIndexes.Select(i => layout.Edges[i]).ToList();

        if (!CanBeSaid(used, nodes))
        {
            return new NoTaxiRoute(NoRouteReason.UnnamedTaxiway);
        }

        return new TaxiRouteFound(new TaxiRoute(
            path.NodeIds,
            TaxiwaysOf(used),
            CrossingsOf(layout, path.NodeIds, used, nodes, runwaysAt),
            holdingPoint.Id,
            path.Meters));
    }

    /// <summary>
    /// The runway an aircraft has to cross to get from where it is to a parking position, when most of the ways to the parkings cross the same
    /// one (<paramref name="minimumShare"/> of the parkings the graph connects to the aircraft): the controller then clears the crossing before
    /// the pilot is told to call the Solo. A parking whose way has no crossing counts against it, and so does one that goes along a runway
    /// instead of across it. Null when there is no such runway, when the aircraft is off the graph, or without taxiways: the caller does
    /// what it does without a map.
    /// </summary>
    /// <returns>The runway as the layout names it ("10L/28R").</returns>
    public static string? CrossingToApron(AirportLayout layout, GeoPoint from, double minimumShare = 0.8)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(from);

        if (!layout.HasTaxiways)
        {
            return null;
        }

        var nodes = layout.Nodes.ToDictionary(n => n.Id);

        var start = layout.Nodes
            .Select(n => (Node: n, Meters: MetersBetween(from, n.Position)))
            .OrderBy(x => x.Meters)
            .FirstOrDefault();

        if (start.Node is null || start.Meters > MaxDistanceToGraphMeters)
        {
            return null;
        }

        var runwaysAt = RunwaysAtNodes(layout);
        var tree = ShortestTreeFrom(layout, nodes, start.Node.Id, null);
        var reached = 0;
        var firstCrossings = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in layout.Parkings.Where(p => p.Node is not null).Select(p => p.Node!.Value).Distinct())
        {
            if (tree.PathTo(node) is not { } path)
            {
                continue;
            }

            reached++;

            var used = path.EdgeIndexes.Select(i => layout.Edges[i]).ToList();

            if (CrossingsOf(layout, path.NodeIds, used, nodes, runwaysAt) is [{ Along: false } first, ..])
            {
                firstCrossings[first.Runway] = firstCrossings.GetValueOrDefault(first.Runway) + 1;
            }
        }

        var top = firstCrossings.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault();

        return reached > 0 && top.Key is not null && top.Value >= minimumShare * reached ? top.Key : null;
    }

    // For each node, the runways whose edges touch it (the nodes where the route is on a runway).
    private static Dictionary<int, SortedSet<string>> RunwaysAtNodes(AirportLayout layout)
    {
        var runwaysAt = new Dictionary<int, SortedSet<string>>();

        foreach (var edge in layout.Edges.Where(e => e.Kind == TaxiEdgeKind.Runway && !string.IsNullOrWhiteSpace(e.Runway)))
        {
            foreach (var id in new[] { edge.From, edge.To })
            {
                if (!runwaysAt.TryGetValue(id, out var set))
                {
                    runwaysAt[id] = set = new SortedSet<string>(StringComparer.Ordinal);
                }

                set.Add(edge.Runway!);
            }
        }

        return runwaysAt;
    }

    // The holding point of an end: a holding point the layout ties to that runway (the simulator marks them 80 to 130 m from the runway and not
    // next to it in the graph, so they are tied by distance when the layout is made), the one nearest to the threshold of that end.
    private static TaxiNode? HoldingPointOf(AirportLayout layout, LayoutRunway runway, LayoutRunwayEnd end) =>
        layout.Nodes
            .Where(n => n.Kind == TaxiNodeKind.HoldShort && runway.Matches(n.Runway))
            .OrderBy(n => MetersBetween(n.Position, end.Position))
            .ThenBy(n => n.Id)
            .FirstOrDefault();

    private sealed record Path(IReadOnlyList<int> NodeIds, IReadOnlyList<int> EdgeIndexes, double Meters);

    // What Dijkstra leaves: for each node reached, the node and the edge it was reached by, from which the way to it is rebuilt.
    private sealed record ShortestTree(
        AirportLayout Layout,
        Dictionary<int, TaxiNode> Nodes,
        int From,
        HashSet<int> Settled,
        Dictionary<int, (int Previous, int EdgeIndex)> CameBy)
    {
        public Path? PathTo(int to)
        {
            if (!Settled.Contains(to))
            {
                return null;
            }

            var nodeIds = new List<int> { to };
            var edgeIndexes = new List<int>();

            for (var at = to; at != From;)
            {
                var (previous, edgeIndex) = CameBy[at];

                edgeIndexes.Add(edgeIndex);
                nodeIds.Add(previous);
                at = previous;
            }

            nodeIds.Reverse();
            edgeIndexes.Reverse();

            var length = edgeIndexes.Sum(i => MetersBetween(Nodes[Layout.Edges[i].From].Position, Nodes[Layout.Edges[i].To].Position));

            return new Path(nodeIds, edgeIndexes, length);
        }
    }

    private static Path? ShortestPath(AirportLayout layout, Dictionary<int, TaxiNode> nodes, int from, int to) =>
        ShortestTreeFrom(layout, nodes, from, to).PathTo(to);

    // Dijkstra over the graph taken as two-way. The weight is the length of the edge, plus a penalty for a runway or a taxiway with no name.
    // Stops when it reaches stopAt; without one it settles every node it can reach.
    private static ShortestTree ShortestTreeFrom(AirportLayout layout, Dictionary<int, TaxiNode> nodes, int from, int? stopAt)
    {
        var adjacency = nodes.Keys.ToDictionary(id => id, _ => new List<(int Neighbor, int EdgeIndex, double Cost)>());

        for (var i = 0; i < layout.Edges.Count; i++)
        {
            var edge = layout.Edges[i];
            var meters = MetersBetween(nodes[edge.From].Position, nodes[edge.To].Position);
            var cost = meters + Penalty(edge);

            adjacency[edge.From].Add((edge.To, i, cost));
            adjacency[edge.To].Add((edge.From, i, cost));
        }

        var best = new Dictionary<int, double> { [from] = 0 };
        var cameBy = new Dictionary<int, (int Previous, int EdgeIndex)>();
        var queue = new PriorityQueue<int, double>();
        var settled = new HashSet<int>();

        queue.Enqueue(from, 0);

        while (queue.TryDequeue(out var current, out var cost))
        {
            if (!settled.Add(current))
            {
                continue;
            }

            if (current == stopAt)
            {
                break;
            }

            foreach (var (neighbor, edgeIndex, step) in adjacency[current])
            {
                var through = cost + step;

                if (!best.TryGetValue(neighbor, out var known) || through < known)
                {
                    best[neighbor] = through;
                    cameBy[neighbor] = (current, edgeIndex);
                    queue.Enqueue(neighbor, through);
                }
            }
        }

        return new ShortestTree(layout, nodes, from, settled, cameBy);
    }

    private static double Penalty(TaxiEdge edge) => edge.Kind switch
    {
        TaxiEdgeKind.Runway => RunwayEdgePenaltyMeters,
        TaxiEdgeKind.Taxiway when string.IsNullOrWhiteSpace(edge.Name) => UnnamedTaxiwayPenaltyMeters,
        _ => 0,
    };

    // A route is said when the taxiways that have a name are most of it: at least one, no more than MaxUnnamedShare of the taxiway length without
    // a name, and no run of unnamed stretches longer than MaxUnnamedRunMeters.
    private static bool CanBeSaid(List<TaxiEdge> used, Dictionary<int, TaxiNode> nodes)
    {
        var taxiwayMeters = 0.0;
        var unnamedMeters = 0.0;
        var run = 0.0;
        var longestRun = 0.0;
        var named = 0;

        foreach (var edge in used)
        {
            if (edge.Kind != TaxiEdgeKind.Taxiway)
            {
                run = 0;
                continue;
            }

            var meters = MetersBetween(nodes[edge.From].Position, nodes[edge.To].Position);

            taxiwayMeters += meters;

            if (string.IsNullOrWhiteSpace(edge.Name))
            {
                unnamedMeters += meters;
                run += meters;
                longestRun = Math.Max(longestRun, run);
            }
            else
            {
                named++;
                run = 0;
            }
        }

        return unnamedMeters == 0 || (named > 0 && unnamedMeters / taxiwayMeters <= MaxUnnamedShare && longestRun <= MaxUnnamedRunMeters);
    }

    // The names in order, a taxiway said once however many stretches it has. The way from a parking position and the runways are not taxiways,
    // and a stretch without a name is gone through and not said.
    private static List<string> TaxiwaysOf(List<TaxiEdge> used)
    {
        var names = new List<string>();

        foreach (var name in used.Where(e => e.Kind == TaxiEdgeKind.Taxiway && !string.IsNullOrWhiteSpace(e.Name)).Select(e => e.Name!))
        {
            if (names.Count == 0 || names[^1] != name)
            {
                names.Add(name);
            }
        }

        return names;
    }

    // Each time the route reaches a runway (not the one the aircraft starts on) is one crossing, however many of its nodes the route touches.
    private static List<RunwayCrossing> CrossingsOf(
        AirportLayout layout,
        IReadOnlyList<int> path,
        List<TaxiEdge> used,
        Dictionary<int, TaxiNode> nodes,
        Dictionary<int, SortedSet<string>> runwaysAt)
    {
        var crossings = new List<RunwayCrossing>();

        for (var i = 1; i < path.Count; i++)
        {
            if (!runwaysAt.TryGetValue(path[i], out var here))
            {
                continue;
            }

            foreach (var name in here)
            {
                if (runwaysAt.TryGetValue(path[i - 1], out var before) && before.Contains(name))
                {
                    continue;
                }

                var along = false;

                for (var j = i; j + 1 < path.Count && runwaysAt.TryGetValue(path[j + 1], out var next) && next.Contains(name); j++)
                {
                    along |= used[j] is { Kind: TaxiEdgeKind.Runway } edge && edge.Runway == name;
                }

                crossings.Add(new RunwayCrossing(name, path[i], HoldShortBefore(layout, path, i, name, nodes, runwaysAt), along));
            }
        }

        return crossings;
    }

    // The holding point of the runway the route is about to reach: the last one tied to it on the way there, looking back along the route until
    // another runway is on it. Null when the graph marks none (then the crossing is there, and the place to stop is not known).
    private static int? HoldShortBefore(
        AirportLayout layout,
        IReadOnlyList<int> path,
        int reached,
        string runway,
        Dictionary<int, TaxiNode> nodes,
        Dictionary<int, SortedSet<string>> runwaysAt)
    {
        for (var i = reached - 1; i >= 0 && !runwaysAt.ContainsKey(path[i]); i--)
        {
            if (nodes[path[i]] is { Kind: TaxiNodeKind.HoldShort } node && LayoutRunway.SameName(node.Runway, runway))
            {
                return node.Id;
            }
        }

        return null;
    }

    private static double MetersBetween(GeoPoint a, GeoPoint b) =>
        Geo.DistanceKm(a.LatitudeDegrees, a.LongitudeDegrees, b.LatitudeDegrees, b.LongitudeDegrees) * 1000;
}
