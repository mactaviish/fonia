using Fonia.Core.Airports;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Airports;

// The places of the stored map where an aircraft can be put without a simulator (the toy airport: runways 11/29 and 15/33).
public class PracticePlacesTests
{
    private static readonly AirportLayout Toy = TestLayouts.Toy();

    private static GeoPoint Node(int id) => Toy.Nodes.Single(n => n.Id == id).Position;

    [Fact]
    public void A_parking_is_found_by_its_number()
    {
        var place = PracticePlaces.Parking(Toy, 1);

        place.ShouldNotBeNull();
        place.Position.ShouldBe(new GeoPoint(-25.5010, -49.1060));
        place.Description.ShouldBe("PATIO 1 (vaga 1)");
        PracticePlaces.Parking(Toy, 9).ShouldBeNull();
    }

    [Theory]
    [InlineData("15", 5)]
    [InlineData("33", 5)]
    [InlineData("11", 2)]
    [InlineData("29", 4)]
    public void The_holding_point_is_the_one_of_the_runway_nearest_to_the_end_asked(string designator, int node)
    {
        var place = PracticePlaces.HoldingPoint(Toy, designator);

        place.ShouldNotBeNull();
        place.Position.ShouldBe(Node(node));
        place.Description.ShouldBe($"ponto de espera da pista {designator}");
    }

    [Fact]
    public void An_end_or_a_holding_point_the_map_does_not_have_is_none()
    {
        PracticePlaces.HoldingPoint(Toy, "09").ShouldBeNull();
        PracticePlaces.HoldingPoint(Toy with { Nodes = [.. Toy.Nodes.Where(n => n.Kind != TaxiNodeKind.HoldShort)] }, "15").ShouldBeNull();
    }

    [Fact]
    public void The_threshold_is_where_the_map_has_the_end()
    {
        PracticePlaces.Threshold(Toy, "29")!.Position.ShouldBe(new GeoPoint(-25.5040, -49.1000));
        PracticePlaces.Threshold(Toy, "29")!.Description.ShouldBe("cabeceira da pista 29");
        PracticePlaces.Threshold(Toy, "09").ShouldBeNull();
    }
}
