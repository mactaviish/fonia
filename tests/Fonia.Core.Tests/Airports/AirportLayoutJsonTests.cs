using System.Text.Json.Nodes;
using Fonia.Core.Airports;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Airports;

public class AirportLayoutJsonTests
{
    private static AirportLayout RoundTrip(AirportLayout layout) => AirportLayoutJson.Read(AirportLayoutJson.Write(layout));

    // The lists of a record compare by reference, and Shouldly's equivalence by the type of the list: so, element by element.
    private static void ShouldMatch(AirportLayout actual, AirportLayout expected)
    {
        actual.Ident.ShouldBe(expected.Ident);
        actual.Runways.ShouldBe(expected.Runways);
        actual.Nodes.ShouldBe(expected.Nodes);
        actual.Edges.ShouldBe(expected.Edges);
        actual.Parkings.ShouldBe(expected.Parkings);
        actual.Frequencies.ShouldBe(expected.Frequencies);
    }

    // The edit of a written layout, to break one thing at a time.
    private static string Broken(Action<JsonObject> edit)
    {
        var json = JsonNode.Parse(AirportLayoutJson.Write(TestLayouts.Toy()))!.AsObject();

        edit(json);

        return json.ToJsonString();
    }

    [Fact]
    public void A_layout_with_two_runways_and_a_taxiway_that_crosses_one_goes_through_the_json_without_losing_anything()
    {
        var layout = TestLayouts.Toy();

        var back = RoundTrip(layout);

        ShouldMatch(back, layout);
        back.Runways.Count.ShouldBe(2);
        back.Nodes.Count.ShouldBe(9);
        back.Edges.Count.ShouldBe(8);
    }

    [Fact]
    public void Writing_what_was_read_gives_the_same_text()
    {
        var text = AirportLayoutJson.Write(TestLayouts.Toy());

        AirportLayoutJson.Write(AirportLayoutJson.Read(text)).ShouldBe(text);
    }

    [Fact]
    public void The_frequencies_keep_their_type_and_the_exact_khz()
    {
        var frequencies = RoundTrip(TestLayouts.Toy()).Frequencies;

        frequencies.Select(f => (f.Type, f.Frequency.KHz)).ShouldBe(
            [(AirportFrequencyType.Ground, 121900), (AirportFrequencyType.Tower, 118275), (AirportFrequencyType.Approach, 119950), (AirportFrequencyType.Atis, 127800)]);
        frequencies[1].Description.ShouldBe("TORRE");
    }

    [Fact]
    public void Edges_without_a_name_stay_without_one_and_a_runway_edge_keeps_its_runway()
    {
        var edges = RoundTrip(TestLayouts.Toy()).Edges;

        edges.Where(e => e.Kind == TaxiEdgeKind.Runway).ShouldAllBe(e => e.Name == null && e.Runway != null);
        edges.Where(e => e.Kind == TaxiEdgeKind.Taxiway).ShouldAllBe(e => e.Name != null && e.Runway == null);
    }

    // A scenery without taxiways (the case of today's behaviour) is a valid layout, just a poor one.
    [Fact]
    public void A_layout_with_only_runways_is_valid_and_says_it_has_no_taxiways()
    {
        var back = RoundTrip(TestLayouts.RunwaysOnly());

        back.Runways.Count.ShouldBe(2);
        back.HasTaxiways.ShouldBeFalse();
        TestLayouts.Toy().HasTaxiways.ShouldBeTrue();
    }

    [Fact]
    public void The_file_says_which_version_of_the_format_it_is()
    {
        JsonNode.Parse(AirportLayoutJson.Write(TestLayouts.Toy()))!["version"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public void A_field_the_reader_does_not_know_is_ignored()
    {
        var json = Broken(o => o["somethingNew"] = "x");

        ShouldMatch(AirportLayoutJson.Read(json), TestLayouts.Toy());
    }

    [Fact]
    public void A_version_the_reader_does_not_know_is_refused()
    {
        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Read(Broken(o => o["version"] = 2)))
            .Message.ShouldContain("version");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"ident\":\"SBXX\"}")]
    public void Text_that_is_not_a_layout_is_refused_with_the_layout_exception(string json)
    {
        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Read(json));
    }

    [Fact]
    public void A_layout_without_an_ident_is_refused()
    {
        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Read(Broken(o => o["ident"] = " ")));
    }

    [Fact]
    public void Two_nodes_with_the_same_id_are_refused()
    {
        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Read(Broken(o => o["nodes"]![1]!["id"] = 1)))
            .Message.ShouldContain("node");
    }

    [Fact]
    public void An_edge_to_a_node_that_does_not_exist_is_refused()
    {
        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Read(Broken(o => o["edges"]![0]!["to"] = 99)))
            .Message.ShouldContain("99");
    }

    [Fact]
    public void An_edge_from_a_node_to_itself_is_refused()
    {
        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Read(Broken(o => o["edges"]![0]!["to"] = o["edges"]![0]!["from"]!.GetValue<int>())));
    }

    [Fact]
    public void A_runway_edge_without_its_runway_is_refused()
    {
        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Read(Broken(o => o["edges"]![5]!["runway"] = null)));
    }

    [Fact]
    public void A_holding_point_keeps_the_runway_it_is_tied_to()
    {
        var nodes = RoundTrip(TestLayouts.Toy()).Nodes;

        nodes.Single(n => n.Id == 5).Runway.ShouldBe("15/33");
        nodes.Single(n => n.Id == 1).Runway.ShouldBeNull();
    }

    [Fact]
    public void A_holding_point_tied_to_a_runway_the_layout_does_not_have_is_refused()
    {
        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Read(Broken(o => o["nodes"]![4]!["runway"] = "09/27")))
            .Message.ShouldContain("09/27");
    }

    [Fact]
    public void The_runway_of_a_holding_point_may_be_written_in_the_other_order()
    {
        AirportLayoutJson.Read(Broken(o => o["nodes"]![4]!["runway"] = "33/15")).Nodes.Single(n => n.Id == 5).Runway.ShouldBe("33/15");
    }

    [Fact]
    public void A_parking_that_enters_the_graph_at_a_missing_node_is_refused()
    {
        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Read(Broken(o => o["parkings"]![0]!["node"] = 99)));
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public void A_position_that_is_not_on_the_earth_is_refused(double latitude, double longitude)
    {
        var json = Broken(o =>
        {
            o["nodes"]![0]!["position"]!["latitudeDegrees"] = latitude;
            o["nodes"]![0]!["position"]!["longitudeDegrees"] = longitude;
        });

        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Read(json));
    }

    [Fact]
    public void A_runway_without_a_length_is_refused()
    {
        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Read(Broken(o => o["runways"]![0]!["lengthMeters"] = 0)));
    }

    [Fact]
    public void A_frequency_that_is_not_positive_is_refused()
    {
        var json = Broken(o => o["frequencies"]![0]!["frequency"] = 0);

        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Read(json));
    }

    [Fact]
    public void Writing_a_layout_that_could_not_be_read_back_is_refused_too()
    {
        var broken = TestLayouts.Toy() with { Edges = [new TaxiEdge(1, 99, "A", TaxiEdgeKind.Taxiway, null)] };

        Should.Throw<AirportLayoutFormatException>(() => AirportLayoutJson.Write(broken));
    }
}
