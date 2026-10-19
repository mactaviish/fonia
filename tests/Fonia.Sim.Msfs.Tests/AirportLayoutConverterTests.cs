using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Sim.Msfs.Tests;

// The raw data of MSFS into the app's layout, built the way the real dumps of SBCT, SBGR and others showed it: offsets in meters east (X) and
// north (Z), taxiways as paths of type 4, holding points 100 m out from the runway, the taxi point first and the parking spot second in a
// parking path.
public class AirportLayoutConverterTests
{
    private const double ReferenceLatitude = -25.5;
    private const double ReferenceLongitude = -49.1;
    private const double MetersPerDegreeLatitude = 111_320;

    private static readonly double MetersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(ReferenceLatitude * Math.PI / 180);

    private static GeoPoint At(double east, double north) =>
        new(ReferenceLatitude + (north / MetersPerDegreeLatitude), ReferenceLongitude + (east / MetersPerDegreeLongitude));

    private static readonly RawAirport Airport = new("SBXX", "SB", "Test", ReferenceLatitude, ReferenceLongitude, 900, 0, 1, 0, 0, 0, 0, 0);

    // Runway 15/33 through the reference point: 1000 m long, heading 315 degrees (the heading of the primary end, 33).
    private static readonly RawRunway Runway33 = new(ReferenceLatitude, ReferenceLongitude, 900, 315, 1000, 45, 4, 33, 0, 15, 0);

    private static RawTaxiPoint Point(int index, int type, double east, double north) => new(index, type, 0, (float)east, (float)north);

    private static RawTaxiPath Path(int type, int start, int end, uint name = 0, int runwayNumber = 0, int runwayDesignator = 0) =>
        new(type, 25, runwayNumber, runwayDesignator, start, end, name);

    private static RawParking Spot(int index, int type, int name, uint number, double east, double north) =>
        new(index, type, 1, name, 0, number, 0, 180, 20, (float)east, (float)north);

    private static AirportFacility Facility(
        RawRunway[]? runways = null,
        RawTaxiPoint[]? points = null,
        RawTaxiPath[]? paths = null,
        RawTaxiName[]? names = null,
        RawParking[]? parkings = null,
        RawFrequency[]? frequencies = null) =>
        new(Airport, runways ?? [Runway33], frequencies ?? [], points ?? [], paths ?? [], names ?? [new RawTaxiName(0, string.Empty), new RawTaxiName(1, "A")], parkings ?? [], [], TimeSpan.Zero);

    // The small airport of the tests: a parking spot, taxiway A north along E=320 to a holding point 100 m from the runway, which has a chain of
    // runway nodes (5, 6, 8, 7) along it, and a connector from the holding point to the runway.
    private static AirportFacility SmallAirport() =>
        Facility(
            points:
            [
                Point(0, 1, 320, -690),
                Point(1, 1, 320, -500),
                Point(2, 5, 320, -180),
                Point(5, 1, -353, 353),
                Point(6, 1, 0, 0),
                Point(8, 1, 250, -250),
                Point(7, 1, 353, -353),
            ],
            paths:
            [
                Path(3, 0, 0),
                Path(4, 0, 1, 1),
                Path(4, 1, 2, 1),
                Path(4, 2, 8, 0),
                Path(2, 5, 6, 0, 33),
                Path(2, 6, 8, 0, 33),
                Path(2, 8, 7, 0, 33),
            ],
            parkings: [Spot(0, 3, 1, 1, 320, -700)]);

    [Fact]
    public void The_runway_has_its_lower_number_first_and_the_thresholds_at_the_start_of_each_run()
    {
        var layout = AirportLayoutConverter.Convert(SmallAirport()).Layout!;

        var runway = layout.Runways.Single();
        runway.Name.ShouldBe("15/33");
        runway.LengthMeters.ShouldBe(1000);

        // Heading 315 points north-west: the primary end (33) starts behind the center, to the south-east; the secondary end (15), to the north-west.
        runway.High.Designator.ShouldBe("33");
        runway.High.TrueHeadingDegrees.ShouldBe(315, 0.01);
        runway.High.Position.LatitudeDegrees.ShouldBeLessThan(ReferenceLatitude);
        runway.High.Position.LongitudeDegrees.ShouldBeGreaterThan(ReferenceLongitude);
        runway.Low.Designator.ShouldBe("15");
        runway.Low.TrueHeadingDegrees.ShouldBe(135, 0.01);
        runway.Low.Position.LatitudeDegrees.ShouldBeGreaterThan(ReferenceLatitude);
        runway.Low.Position.LongitudeDegrees.ShouldBeLessThan(ReferenceLongitude);
    }

    [Fact]
    public void Runways_with_a_side_have_it_in_the_designator()
    {
        var parallel = new RawRunway(ReferenceLatitude, ReferenceLongitude, 900, 74, 3700, 60, 4, 10, 1, 28, 2);

        var layout = AirportLayoutConverter.Convert(Facility(runways: [parallel])).Layout!;

        layout.Runways.Single().Name.ShouldBe("10L/28R");
    }

    [Fact]
    public void A_runway_with_a_number_that_is_not_1_to_36_is_left_out_and_the_airport_is_refused_without_another()
    {
        var odd = Runway33 with { PrimaryNumber = 40, SecondaryNumber = 44 };

        var conversion = AirportLayoutConverter.Convert(Facility(runways: [odd]));

        conversion.Layout.ShouldBeNull();
        conversion.Notes.ShouldContain(n => n.Contains("not 1 to 36", StringComparison.Ordinal));
    }

    [Fact]
    public void The_taxi_points_are_nodes_with_the_index_of_the_simulator_and_their_offsets_become_positions()
    {
        var layout = AirportLayoutConverter.Convert(SmallAirport()).Layout!;

        var node = layout.Nodes.Single(n => n.Id == 1);
        node.Position.LatitudeDegrees.ShouldBe(At(320, -500).LatitudeDegrees, 1e-9);
        node.Position.LongitudeDegrees.ShouldBe(At(320, -500).LongitudeDegrees, 1e-9);
        node.Kind.ShouldBe(TaxiNodeKind.Normal);
    }

    [Theory]
    [InlineData(1, TaxiNodeKind.Normal)]
    [InlineData(2, TaxiNodeKind.HoldShort)]
    [InlineData(4, TaxiNodeKind.HoldShort)]
    [InlineData(5, TaxiNodeKind.HoldShort)]
    [InlineData(6, TaxiNodeKind.HoldShort)]
    [InlineData(0, TaxiNodeKind.Normal)]
    public void The_type_of_a_taxi_point_makes_it_a_holding_point_or_not(int type, TaxiNodeKind expected)
    {
        var layout = AirportLayoutConverter.Convert(Facility(points: [Point(3, type, 320, -180)])).Layout!;

        layout.Nodes.Single(n => n.Id == 3).Kind.ShouldBe(expected);
    }

    [Fact]
    public void A_holding_point_is_tied_to_the_runway_nearest_to_it()
    {
        var layout = AirportLayoutConverter.Convert(SmallAirport()).Layout!;

        layout.Nodes.Single(n => n.Id == 2).Runway.ShouldBe("15/33");
        layout.Nodes.Where(n => n.Id != 2).ShouldAllBe(n => n.Runway == null);
    }

    [Fact]
    public void A_holding_point_far_from_every_runway_is_tied_to_none_and_the_notes_say_so()
    {
        var conversion = AirportLayoutConverter.Convert(Facility(points: [Point(0, 5, 320, -180), Point(1, 5, 2000, 2000)]));

        conversion.Layout!.Nodes.Single(n => n.Id == 0).Runway.ShouldBe("15/33");
        conversion.Layout.Nodes.Single(n => n.Id == 1).Runway.ShouldBeNull();
        conversion.Notes.ShouldContain(n => n.Contains("1 of 2 holding points", StringComparison.Ordinal));
    }

    [Fact]
    public void The_nearest_of_two_runways_is_the_one_a_holding_point_is_tied_to()
    {
        // A second runway 11/29 running east-west, 600 m south of the first.
        var second = new RawRunway(At(0, -600).LatitudeDegrees, ReferenceLongitude, 900, 92, 1800, 45, 4, 11, 0, 29, 0);

        var layout = AirportLayoutConverter.Convert(Facility(runways: [Runway33, second], points: [Point(0, 5, 0, -500), Point(1, 5, 0, 100)])).Layout!;

        layout.Nodes.Single(n => n.Id == 0).Runway.ShouldBe("11/29");
        layout.Nodes.Single(n => n.Id == 1).Runway.ShouldBe("15/33");
    }

    [Fact]
    public void Taxiway_paths_are_edges_with_their_name_and_a_path_without_a_name_has_none()
    {
        var layout = AirportLayoutConverter.Convert(SmallAirport()).Layout!;

        layout.Edges.Where(e => e.Kind == TaxiEdgeKind.Taxiway).Select(e => (e.From, e.To, e.Name)).ShouldBe([(0, 1, "A"), (1, 2, "A"), (2, 8, null)]);
        layout.HasTaxiways.ShouldBeTrue();
    }

    [Fact]
    public void The_paths_of_type_1_are_taxiways_too()
    {
        var layout = AirportLayoutConverter.Convert(Facility(points: [Point(0, 1, 0, 0), Point(1, 1, 10, 10)], paths: [Path(1, 0, 1, 1)])).Layout!;

        layout.Edges.Single().ShouldBe(new TaxiEdge(0, 1, "A", TaxiEdgeKind.Taxiway, null));
    }

    [Fact]
    public void A_runway_path_is_an_edge_of_the_runway_it_belongs_to_by_number_and_side()
    {
        var layout = AirportLayoutConverter.Convert(SmallAirport()).Layout!;

        layout.Edges.Where(e => e.Kind == TaxiEdgeKind.Runway).Select(e => (e.From, e.To, e.Runway)).ShouldBe([(5, 6, "15/33"), (6, 8, "15/33"), (8, 7, "15/33")]);
    }

    [Fact]
    public void A_runway_path_of_a_runway_the_airport_does_not_have_is_left_out_and_noted()
    {
        var conversion = AirportLayoutConverter.Convert(Facility(points: [Point(0, 1, 0, 0), Point(1, 1, 10, 10)], paths: [Path(2, 0, 1, 0, 9)]));

        conversion.Layout!.Edges.ShouldBeEmpty();
        conversion.Notes.ShouldContain(n => n.Contains("1 paths refer to", StringComparison.Ordinal));
    }

    [Fact]
    public void A_parking_path_joins_the_taxi_point_to_a_node_for_the_parking_spot()
    {
        var layout = AirportLayoutConverter.Convert(SmallAirport()).Layout!;

        var spot = layout.Parkings.Single();
        spot.Node.ShouldBe(1_000_000);
        spot.Number.ShouldBe(1);
        layout.Nodes.Single(n => n.Id == 1_000_000).Kind.ShouldBe(TaxiNodeKind.Parking);
        layout.Edges.Single(e => e.Kind == TaxiEdgeKind.Parking).ShouldBe(new TaxiEdge(0, 1_000_000, null, TaxiEdgeKind.Parking, null));
    }

    [Fact]
    public void A_parking_spot_with_no_path_has_no_node()
    {
        var layout = AirportLayoutConverter.Convert(Facility(parkings: [Spot(0, 3, 1, 1, 320, -700)])).Layout!;

        layout.Parkings.Single().Node.ShouldBeNull();
        layout.Nodes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(3, 1, "PARKING", ParkingKind.Ramp)]
    [InlineData(8, 10, "GATE", ParkingKind.Gate)]
    [InlineData(10, 12, "GATE A", ParkingKind.Gate)]
    [InlineData(11, 11, "DOCK", ParkingKind.Dock)]
    [InlineData(13, 1, "PARKING", ParkingKind.Other)]
    public void Parking_spots_have_the_name_and_kind_the_sdk_codes_say(int type, int name, string expectedName, ParkingKind expectedKind)
    {
        var layout = AirportLayoutConverter.Convert(Facility(parkings: [Spot(0, type, name, 5, 0, 0)])).Layout!;

        layout.Parkings.Single().Name.ShouldBe(expectedName);
        layout.Parkings.Single().Kind.ShouldBe(expectedKind);
    }

    [Fact]
    public void Paths_that_are_not_for_aircraft_are_left_out_and_counted()
    {
        var conversion = AirportConversionOf(Path(6, 0, 1), Path(7, 0, 1), Path(5, 0, 1), Path(6, 1, 0));

        conversion.Layout!.Edges.ShouldBeEmpty();
        conversion.Notes.ShouldContain(n => n.Contains("2 of type 6 (vehicle)", StringComparison.Ordinal) && n.Contains("1 of type 7 (road)", StringComparison.Ordinal) && n.Contains("1 of type 5 (closed)", StringComparison.Ordinal));
    }

    private static LayoutConversion AirportConversionOf(params RawTaxiPath[] paths) =>
        AirportLayoutConverter.Convert(Facility(points: [Point(0, 1, 0, 0), Point(1, 1, 10, 10)], paths: paths));

    [Fact]
    public void A_path_to_a_point_the_data_does_not_have_is_left_out_and_noted()
    {
        var conversion = AirportConversionOf(Path(4, 0, 9, 1));

        conversion.Layout!.Edges.ShouldBeEmpty();
        conversion.Notes.ShouldContain(n => n.Contains("1 paths refer to", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_stretch_given_twice_is_one_edge_and_in_either_direction()
    {
        var conversion = AirportConversionOf(Path(4, 0, 1, 1), Path(4, 0, 1, 1), Path(4, 1, 0, 1));

        conversion.Layout!.Edges.Count.ShouldBe(1);
    }

    [Fact]
    public void The_notes_say_how_many_taxiway_paths_have_no_name()
    {
        var conversion = AirportConversionOf(Path(4, 0, 1, 0), Path(4, 1, 0, 1));

        conversion.Notes.ShouldContain("1 of 2 taxiway paths have no name; a route over one of them cannot be spoken.");
    }

    [Fact]
    public void The_frequencies_become_the_apps_types_with_the_approach_and_the_departure_both_the_controle()
    {
        var layout = AirportLayoutConverter.Convert(Facility(frequencies:
        [
            new RawFrequency(6, 118_150_000, "TWR"),
            new RawFrequency(5, 121_700_000, "GND"),
            new RawFrequency(8, 119_700_000, "APP"),
            new RawFrequency(9, 119_950_000, "DEP"),
            new RawFrequency(1, 127_800_000, "ATIS"),
            new RawFrequency(7, 119_300_000, "CLD"),
            new RawFrequency(3, 131_825_000, "UNICOM"),
            new RawFrequency(10, 125_000_000, "CENTER"),
            new RawFrequency(6, 0, "BROKEN"),
        ])).Layout!;

        layout.Frequencies.Select(f => (f.Type, f.Frequency.KHz)).ShouldBe(
        [
            (AirportFrequencyType.Tower, 118150),
            (AirportFrequencyType.Ground, 121700),
            (AirportFrequencyType.Approach, 119700),
            (AirportFrequencyType.Approach, 119950),
            (AirportFrequencyType.Atis, 127800),
            (AirportFrequencyType.Delivery, 119300),
            (AirportFrequencyType.Radio, 131825),
            (AirportFrequencyType.Other, 125000),
        ]);
    }

    [Fact]
    public void Without_the_airport_or_without_a_runway_there_is_no_layout()
    {
        AirportLayoutConverter.Convert(SmallAirport() with { Airport = null }).Layout.ShouldBeNull();
        AirportLayoutConverter.Convert(SmallAirport() with { Runways = [] }).Layout.ShouldBeNull();
    }

    [Fact]
    public void What_is_made_goes_through_the_json_of_the_layout_and_back()
    {
        var layout = AirportLayoutConverter.Convert(SmallAirport()).Layout!;

        var back = AirportLayoutJson.Read(AirportLayoutJson.Write(layout));

        back.Nodes.ShouldBe(layout.Nodes);
        back.Edges.ShouldBe(layout.Edges);
        back.Runways.ShouldBe(layout.Runways);
    }

    // The whole way: the data of the simulator, the layout made from it, and a route over it from the parking spot to the holding point of 15.
    [Fact]
    public void A_route_is_found_over_a_layout_made_from_the_simulators_data()
    {
        var layout = AirportLayoutConverter.Convert(SmallAirport()).Layout!;

        var found = TaxiRouter.Find(layout, At(320, -700), "15").ShouldBeOfType<TaxiRouteFound>().Route;

        found.NodeIds.ShouldBe([1_000_000, 0, 1, 2]);
        found.Taxiways.ShouldBe(["A"]);
        found.HoldingPointNodeId.ShouldBe(2);
        found.Crossings.ShouldBeEmpty();
    }

    [Fact]
    public void A_route_over_a_taxiway_the_simulator_did_not_name_is_no_route()
    {
        var unnamed = SmallAirport() with
        {
            TaxiPaths = [.. SmallAirport().TaxiPaths.Select(p => p is { Type: 4, Start: 1 } ? p with { NameIndex = 0 } : p)],
        };

        var layout = AirportLayoutConverter.Convert(unnamed).Layout!;

        TaxiRouter.Find(layout, At(320, -700), "15").ShouldBeOfType<NoTaxiRoute>().Reason.ShouldBe(NoRouteReason.UnnamedTaxiway);
    }
}
