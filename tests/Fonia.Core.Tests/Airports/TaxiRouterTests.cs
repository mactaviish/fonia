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
}
