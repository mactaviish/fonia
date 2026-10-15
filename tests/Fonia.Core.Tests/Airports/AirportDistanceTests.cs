using Fonia.Core.Airports;

namespace Fonia.Core.Tests.Airports;

public class AirportDistanceTests
{
    private static Airport At(double latitude, double longitude) =>
        new("SBXX", "X", "X", AirportType.Large, latitude, longitude, null, "SBXX", null, "X", SpokenNameSource.Municipality, false, [], []);

    [Fact]
    public void The_distance_from_an_airport_to_its_own_position_is_zero()
    {
        At(-25.5285, -49.1758).DistanceKmTo(-25.5285, -49.1758).ShouldBe(0, 0.001);
    }

    [Fact]
    public void Curitiba_to_Guarulhos_is_about_359_kilometers()
    {
        // SBCT to SBGR along the great circle (haversine, 6371 km sphere): 359.1 km.
        At(-25.5285, -49.1758).DistanceKmTo(-23.4356, -46.4731).ShouldBe(359.1, 0.5);
    }

    [Fact]
    public void One_degree_of_latitude_is_about_111_kilometers_and_the_distance_does_not_depend_on_the_direction()
    {
        At(0, 0).DistanceKmTo(1, 0).ShouldBe(111.2, 0.3);
        At(1, 0).DistanceKmTo(0, 0).ShouldBe(111.2, 0.3);
    }
}
