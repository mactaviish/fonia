using Fonia.Core.Airports;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Airports;

// The route from where the aircraft is to the holding point of a runway, on the toy layout of TestLayouts: the taxiway from the apron to
// the holding point of 15 crosses runway 11/29. A route that cannot be trusted is no route: the controller then says what it says today.
public class TaxiRouterTests
{
    private static readonly AirportLayout Toy = TestLayouts.Toy();

    private static GeoPoint At(int nodeId) => Toy.Nodes.Single(n => n.Id == nodeId).Position;

    private static TaxiRoute Found(TaxiRouteResult result) => result.ShouldBeOfType<TaxiRouteFound>().Route;

    private static NoRouteReason Refused(TaxiRouteResult result) => result.ShouldBeOfType<NoTaxiRoute>().Reason;

    private static AirportLayout Without(AirportLayout layout, Func<TaxiEdge, bool> remove) => layout with { Edges = [.. layout.Edges.Where(e => !remove(e))] };

    [Fact]
    public void From_the_apron_to_the_holding_point_of_15_the_route_follows_the_taxiways_and_crosses_runway_11_29()
    {
        var route = Found(TaxiRouter.Find(Toy, At(1), "15"));

        route.NodeIds.ShouldBe([1, 2, 3, 4, 5]);
        route.Taxiways.ShouldBe(["A", "B"]);
        route.HoldingPointNodeId.ShouldBe(5);
        route.LengthMeters.ShouldBeGreaterThan(0);
        route.Crossings.ShouldBe([new RunwayCrossing("11/29", 3, 2, false)]);
    }

    [Fact]
    public void A_taxiway_is_named_once_however_many_stretches_it_has()
    {
        Found(TaxiRouter.Find(Toy, At(1), "15")).Taxiways.Count(t => t == "A").ShouldBe(1);
    }

    [Fact]
    public void To_the_holding_point_of_11_there_is_nothing_to_cross()
    {
        var route = Found(TaxiRouter.Find(Toy, At(1), "11"));

        route.NodeIds.ShouldBe([1, 2]);
        route.Taxiways.ShouldBe(["A"]);
        route.Crossings.ShouldBeEmpty();
    }

    [Fact]
    public void Either_end_of_the_runway_finds_its_holding_point()
    {
        Found(TaxiRouter.Find(Toy, At(1), "33")).HoldingPointNodeId.ShouldBe(5);
    }

    [Fact]
    public void A_route_that_starts_at_the_holding_point_before_the_crossing_has_that_point_as_the_place_to_stop()
    {
        var route = Found(TaxiRouter.Find(Toy, At(2), "15"));

        route.NodeIds.ShouldBe([2, 3, 4, 5]);
        route.Crossings.ShouldBe([new RunwayCrossing("11/29", 3, 2, false)]);
    }

    [Fact]
    public void An_aircraft_already_past_the_crossing_has_nothing_to_cross()
    {
        var route = Found(TaxiRouter.Find(Toy, At(4), "15"));

        route.Taxiways.ShouldBe(["B"]);
        route.Crossings.ShouldBeEmpty();
    }

    [Fact]
    public void An_aircraft_already_at_the_holding_point_has_a_route_of_no_length()
    {
        var route = Found(TaxiRouter.Find(Toy, At(5), "15"));

        route.NodeIds.ShouldBe([5]);
        route.Taxiways.ShouldBeEmpty();
        route.LengthMeters.ShouldBe(0);
    }

    [Fact]
    public void A_position_near_a_node_is_taken_as_that_node()
    {
        var nearTheApron = new GeoPoint(At(1).LatitudeDegrees + 0.0003, At(1).LongitudeDegrees);

        Found(TaxiRouter.Find(Toy, nearTheApron, "15")).NodeIds[0].ShouldBe(1);
    }

    [Fact]
    public void An_aircraft_far_from_every_node_is_not_on_the_graph_and_gets_no_route()
    {
        var farAway = new GeoPoint(At(1).LatitudeDegrees + 0.05, At(1).LongitudeDegrees);

        Refused(TaxiRouter.Find(Toy, farAway, "15")).ShouldBe(NoRouteReason.AircraftOffTheGraph);
    }

    [Fact]
    public void A_runway_the_layout_does_not_have_gets_no_route()
    {
        Refused(TaxiRouter.Find(Toy, At(1), "09")).ShouldBe(NoRouteReason.UnknownRunway);
    }

    [Fact]
    public void A_scenery_without_taxiways_gets_no_route()
    {
        Refused(TaxiRouter.Find(TestLayouts.RunwaysOnly(), At(1), "15")).ShouldBe(NoRouteReason.NoTaxiways);
    }

    [Fact]
    public void A_graph_that_is_cut_on_the_way_gets_no_route_and_never_an_invented_one()
    {
        var cut = Without(Toy, e => e is { From: 3, To: 4 });

        Refused(TaxiRouter.Find(cut, At(1), "15")).ShouldBe(NoRouteReason.NotConnected);
    }

    [Fact]
    public void A_runway_without_a_holding_point_in_the_data_gets_no_route()
    {
        var noHoldingPoints = Toy with { Nodes = [.. Toy.Nodes.Select(n => n.Kind == TaxiNodeKind.HoldShort ? n with { Kind = TaxiNodeKind.Normal } : n)] };

        Refused(TaxiRouter.Find(noHoldingPoints, At(1), "15")).ShouldBe(NoRouteReason.NoHoldingPoint);
    }

    [Fact]
    public void A_taxiway_without_a_name_on_the_only_way_gets_no_route()
    {
        var unnamed = Toy with { Edges = [.. Toy.Edges.Select(e => e is { From: 4, To: 5 } ? e with { Name = null } : e)] };

        Refused(TaxiRouter.Find(unnamed, At(1), "15")).ShouldBe(NoRouteReason.UnnamedTaxiway);
    }

    // A route that has names is better than a shorter one that has not: the controller can only say the former.
    [Fact]
    public void A_way_with_names_is_chosen_over_a_shorter_way_without()
    {
        // 2 to 4 around the runway: a short stretch with no name, or a longer one through node 10 with names.
        var layout = Toy with
        {
            Nodes = [.. Toy.Nodes, new TaxiNode(10, new GeoPoint(-25.5015, -49.1030), TaxiNodeKind.Normal)],
            Edges =
            [
                .. Without(Toy, e => e is { From: 2, To: 3 } or { From: 3, To: 4 }).Edges,
                new TaxiEdge(2, 4, null, TaxiEdgeKind.Taxiway, null),
                new TaxiEdge(2, 10, "C", TaxiEdgeKind.Taxiway, null),
                new TaxiEdge(10, 4, "C", TaxiEdgeKind.Taxiway, null),
            ],
        };

        var route = Found(TaxiRouter.Find(layout, At(1), "15"));

        route.NodeIds.ShouldBe([1, 2, 10, 4, 5]);
        route.Taxiways.ShouldBe(["A", "C", "B"]);
        route.Crossings.ShouldBeEmpty();
    }

    // A straight chain of taxiway stretches of the lengths given (meters, north), the last node a holding point of runway 15/33: so the share and
    // the runs of unnamed taxiway on the route are exact. A name of null is a stretch the simulator gave no name.
    private static AirportLayout Chain(params (string? Name, double Meters)[] stretches)
    {
        const double metersPerDegree = 111_320;
        var nodes = new List<TaxiNode> { new(0, new GeoPoint(-25.5, -49.1), TaxiNodeKind.Normal) };
        var edges = new List<TaxiEdge>();
        var north = 0.0;

        for (var i = 0; i < stretches.Length; i++)
        {
            north += stretches[i].Meters;

            var last = i == stretches.Length - 1;
            nodes.Add(new TaxiNode(i + 1, new GeoPoint(-25.5 + (north / metersPerDegree), -49.1), last ? TaxiNodeKind.HoldShort : TaxiNodeKind.Normal, last ? "15/33" : null));
            edges.Add(new TaxiEdge(i, i + 1, stretches[i].Name, TaxiEdgeKind.Taxiway, null));
        }

        return Toy with { Ident = "SBYY", Nodes = nodes, Edges = edges, Parkings = [] };
    }

    private static GeoPoint ChainStart => new(-25.5, -49.1);

    [Fact]
    public void A_short_stretch_without_a_name_between_named_ones_is_gone_through_and_not_said()
    {
        var route = Found(TaxiRouter.Find(Chain(("A", 1000), (null, 20), ("B", 1000)), ChainStart, "15"));

        route.Taxiways.ShouldBe(["A", "B"]);
        route.NodeIds.Count.ShouldBe(4);
    }

    [Fact]
    public void The_unnamed_stretch_of_an_apron_before_the_first_named_taxiway_is_gone_through()
    {
        var route = Found(TaxiRouter.Find(Chain((null, 250), ("A", 900)), ChainStart, "15"));

        route.Taxiways.ShouldBe(["A"]);
    }

    // Short unnamed stretches between named ones, so only the share counts and not the length of a run: 60 m in 160 is 37.5%, 80 m in 180 is 44%.
    private static (string? Name, double Meters)[] Alternating(double namedMeters, double unnamedMeters) =>
        [.. Enumerable.Range(0, 6).SelectMany(_ => new (string?, double)[] { ("A", namedMeters), (null, unnamedMeters) })];

    [Fact]
    public void A_route_with_a_little_less_than_the_most_it_may_have_unnamed_is_said()
    {
        Found(TaxiRouter.Find(Chain(Alternating(100, 60)), ChainStart, "15")).Taxiways.ShouldBe(["A"]);
    }

    [Fact]
    public void A_route_with_more_than_that_unnamed_gets_no_route()
    {
        Refused(TaxiRouter.Find(Chain(Alternating(100, 80)), ChainStart, "15")).ShouldBe(NoRouteReason.UnnamedTaxiway);
    }

    [Fact]
    public void One_long_unnamed_stretch_gets_no_route_however_small_a_share_of_a_long_route_it_is()
    {
        // 320 m without a name is under 10% of 3,320 m, but it is more than a pilot can be left to find alone.
        Refused(TaxiRouter.Find(Chain(("A", 3000), (null, 320)), ChainStart, "15")).ShouldBe(NoRouteReason.UnnamedTaxiway);
    }

    [Fact]
    public void A_route_with_no_named_taxiway_at_all_gets_no_route()
    {
        Refused(TaxiRouter.Find(Chain((null, 100)), ChainStart, "15")).ShouldBe(NoRouteReason.UnnamedTaxiway);
    }

    [Fact]
    public void The_limits_are_those_the_documentation_gives()
    {
        TaxiRouter.MaxUnnamedShare.ShouldBe(0.4);
        TaxiRouter.MaxUnnamedRunMeters.ShouldBe(300);
    }

    // The simulator puts the holding points 80 to 130 m from the runway and not next to it in the graph: they count for a runway by the runway
    // the layout ties them to, wherever they are on the way.
    [Fact]
    public void A_holding_point_that_is_not_tied_to_any_runway_is_not_the_holding_point_of_one()
    {
        var untied = Toy with { Nodes = [.. Toy.Nodes.Select(n => n.Id == 5 ? n with { Runway = null } : n)] };

        Refused(TaxiRouter.Find(untied, At(1), "15")).ShouldBe(NoRouteReason.NoHoldingPoint);
    }

    [Fact]
    public void A_holding_point_tied_to_another_runway_is_not_used()
    {
        var other = Toy with { Nodes = [.. Toy.Nodes.Select(n => n.Id == 5 ? n with { Runway = "11/29" } : n)] };

        Refused(TaxiRouter.Find(other, At(1), "15")).ShouldBe(NoRouteReason.NoHoldingPoint);
    }

    [Fact]
    public void The_runway_of_a_holding_point_can_be_named_in_either_order()
    {
        var reversed = Toy with { Nodes = [.. Toy.Nodes.Select(n => n.Id == 5 ? n with { Runway = "33/15" } : n)] };

        Found(TaxiRouter.Find(reversed, At(1), "15")).HoldingPointNodeId.ShouldBe(5);
    }

    [Fact]
    public void Of_the_holding_points_of_a_runway_the_one_nearest_to_the_threshold_of_the_end_is_used()
    {
        // Runway 11/29 has two (2 and 4), one each side of where taxiway A crosses it: 11 starts near 2, 29 near 4.
        Found(TaxiRouter.Find(Toy, At(1), "11")).HoldingPointNodeId.ShouldBe(2);
        Found(TaxiRouter.Find(Toy, At(1), "29")).HoldingPointNodeId.ShouldBe(4);
    }

    [Fact]
    public void The_place_to_stop_before_a_crossing_is_found_looking_back_past_the_nodes_between()
    {
        // Node 11 between the holding point (2) and the runway (3): the holding point is not the node just before it.
        var farther = Toy with
        {
            Nodes = [.. Toy.Nodes, new TaxiNode(11, new GeoPoint(-25.50175, -49.1050), TaxiNodeKind.Normal)],
            Edges =
            [
                .. Without(Toy, e => e is { From: 2, To: 3 }).Edges,
                new TaxiEdge(2, 11, "A", TaxiEdgeKind.Taxiway, null),
                new TaxiEdge(11, 3, "A", TaxiEdgeKind.Taxiway, null),
            ],
        };

        var route = Found(TaxiRouter.Find(farther, At(1), "15"));

        route.NodeIds.ShouldBe([1, 2, 11, 3, 4, 5]);
        route.Crossings.ShouldBe([new RunwayCrossing("11/29", 3, 2, false)]);
    }

    [Fact]
    public void A_holding_point_of_another_runway_is_not_the_place_to_stop_before_a_crossing()
    {
        var other = Toy with { Nodes = [.. Toy.Nodes.Select(n => n.Id == 2 ? n with { Runway = "15/33" } : n)] };

        Found(TaxiRouter.Find(other, At(1), "15")).Crossings.ShouldBe([new RunwayCrossing("11/29", 3, null, false)]);
    }

    // Following the runway is not crossing it: the route says it is along.
    [Fact]
    public void A_route_that_follows_a_runway_says_so()
    {
        var alongTheRunway = Toy with
        {
            Edges = [.. Toy.Edges.Select(e => e is { From: 3, To: 4 } ? new TaxiEdge(3, 4, null, TaxiEdgeKind.Runway, "11/29") : e)],
        };

        var route = Found(TaxiRouter.Find(alongTheRunway, At(1), "15"));

        route.Taxiways.ShouldBe(["A", "B"]);
        route.Crossings.ShouldBe([new RunwayCrossing("11/29", 3, 2, true)]);
    }

    // The stop before a crossing is only known when the graph marks it: otherwise the crossing is there, with no place to stop.
    [Fact]
    public void A_crossing_with_no_holding_point_before_it_is_reported_without_one()
    {
        var unmarked = Toy with { Nodes = [.. Toy.Nodes.Select(n => n.Id == 2 ? n with { Kind = TaxiNodeKind.Normal } : n)] };

        Found(TaxiRouter.Find(unmarked, At(1), "15")).Crossings.ShouldBe([new RunwayCrossing("11/29", 3, null, false)]);
    }

    [Fact]
    public void An_aircraft_standing_on_a_runway_is_not_crossing_it()
    {
        var route = Found(TaxiRouter.Find(Toy, At(3), "15"));

        route.NodeIds.ShouldBe([3, 4, 5]);
        route.Crossings.ShouldBeEmpty();
    }

    // Where the aircraft that landed on 15/33 goes to the apron: the way crosses 11/29, and the Torre clears that crossing.
    [Fact]
    public void From_the_far_side_of_a_runway_the_way_to_the_apron_crosses_it()
    {
        TaxiRouter.CrossingToApron(Toy, At(5)).ShouldBe("11/29");
    }

    [Fact]
    public void From_the_apron_side_there_is_nothing_to_cross()
    {
        TaxiRouter.CrossingToApron(Toy, At(2)).ShouldBeNull();
    }

    [Fact]
    public void A_crossing_that_not_enough_of_the_parkings_need_is_not_said()
    {
        var withAParkingOnThisSide = Toy with { Parkings = [.. Toy.Parkings, new Parking("PATIO 2", 2, At(5), 5, ParkingKind.Ramp)] };

        TaxiRouter.CrossingToApron(withAParkingOnThisSide, At(5)).ShouldBeNull();
        TaxiRouter.CrossingToApron(withAParkingOnThisSide, At(5), 0.5).ShouldBe("11/29");
    }

    [Fact]
    public void Without_taxiways_a_parking_to_reach_or_a_place_on_the_graph_no_crossing_is_found()
    {
        TaxiRouter.CrossingToApron(TestLayouts.RunwaysOnly(), At(5)).ShouldBeNull();
        TaxiRouter.CrossingToApron(Toy with { Parkings = [] }, At(5)).ShouldBeNull();
        TaxiRouter.CrossingToApron(Toy, new GeoPoint(-25.4, -49.0)).ShouldBeNull();
    }

    [Fact]
    public void A_parking_the_graph_does_not_connect_does_not_count()
    {
        var cut = Without(Toy, e => e is { From: 3, To: 4 });

        TaxiRouter.CrossingToApron(cut, At(5)).ShouldBeNull();
    }
}
