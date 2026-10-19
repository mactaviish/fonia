using System.Text.Json.Nodes;

namespace Fonia.Sim.Msfs.Tests;

// The offsets of the taxi points are laid on the map one of eight ways; the points of the runway paths show which, because the runways
// have absolute positions. Here the world is made first (a runway off the reference point, at an angle) and the offsets are worked back from
// each convention, so every one has to be recognized.
public class FacilityAxisCheckTests
{
    private const double ReferenceLatitude = -25.5;
    private const double ReferenceLongitude = -49.1;
    private const double MetersPerDegreeLatitude = 111_320;

    private static readonly double MetersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(ReferenceLatitude * Math.PI / 180);

    public static TheoryData<string, int> Conventions() =>
        new()
        {
            { "X east, Z north", 0 },
            { "X east, Z south", 1 },
            { "X west, Z north", 2 },
            { "X west, Z south", 3 },
            { "X north, Z east", 4 },
            { "X north, Z west", 5 },
            { "X south, Z east", 6 },
            { "X south, Z west", 7 },
        };

    // From a position in meters east and north to the offsets (x, z) under convention number <c>.
    private static (float X, float Z) Offsets(int convention, double east, double north) => convention switch
    {
        0 => ((float)east, (float)north),
        1 => ((float)east, (float)-north),
        2 => ((float)-east, (float)north),
        3 => ((float)-east, (float)-north),
        4 => ((float)north, (float)east),
        5 => ((float)north, (float)-east),
        6 => ((float)-north, (float)east),
        _ => ((float)-north, (float)-east),
    };

    private static AirportFacility World(int convention, bool runwayPaths = true)
    {
        var airport = new RawAirport("SBXX", "SB", "Test", ReferenceLatitude, ReferenceLongitude, 900, 0, 1, 0, 3, 0, 2, 0);

        // A runway 1000 m long at 60 degrees, its center 300 m east and 200 m north of the reference point.
        var heading = 60 * Math.PI / 180;
        var centerEast = 300.0;
        var centerNorth = 200.0;
        var runway = new RawRunway(
            ReferenceLatitude + (centerNorth / MetersPerDegreeLatitude),
            ReferenceLongitude + (centerEast / MetersPerDegreeLongitude),
            900,
            60,
            1000,
            45,
            4,
            6,
            0,
            24,
            0);

        // Two points on the runway line (at 30% and at -20% of its length from the center) and one far from it.
        RawTaxiPoint OnRunway(int index, double fraction)
        {
            var (x, z) = Offsets(convention, centerEast + (Math.Sin(heading) * 1000 * fraction), centerNorth + (Math.Cos(heading) * 1000 * fraction));

            return new RawTaxiPoint(index, 1, 0, x, z);
        }

        var (farX, farZ) = Offsets(convention, -900, -700);
        var points = new[] { OnRunway(0, 0.3), OnRunway(1, -0.2), new RawTaxiPoint(2, 1, 0, farX, farZ) };
        var paths = runwayPaths ? new[] { new RawTaxiPath(2, 45, 6, 0, 0, 1, 0), new RawTaxiPath(1, 23, 0, 0, 1, 2, 0) } : new[] { new RawTaxiPath(1, 23, 0, 0, 0, 1, 0) };

        return new AirportFacility(airport, [runway], [], points, paths, [], [], [], TimeSpan.Zero);
    }

    [Theory]
    [MemberData(nameof(Conventions))]
    public void Each_of_the_eight_conventions_is_recognized_from_the_points_on_the_runway(string name, int convention)
    {
        var result = FacilityAxisCheck.Evaluate(World(convention));

        result.ShouldNotBeNull();
        result.Best.Name.ShouldBe(name);
        result.Best.MeanErrorMeters.ShouldBeLessThan(1);
        result.Convincing.ShouldBeTrue();
        result.Points.ShouldBe(2);
        result.Ranking.Count.ShouldBe(8);
    }

    [Fact]
    public void Only_the_points_of_the_runway_paths_count_not_the_ones_elsewhere_on_the_airport()
    {
        // The far point (index 2) is on a taxiway path only: if it counted, no convention would fit the runway.
        FacilityAxisCheck.Evaluate(World(0))!.Points.ShouldBe(2);
    }

    [Fact]
    public void Without_runway_paths_there_is_nothing_to_check_with()
    {
        FacilityAxisCheck.Evaluate(World(0, runwayPaths: false)).ShouldBeNull();
    }

    [Fact]
    public void Without_an_airport_or_a_runway_there_is_nothing_to_check_with()
    {
        FacilityAxisCheck.Evaluate(World(0) with { Airport = null }).ShouldBeNull();
        FacilityAxisCheck.Evaluate(World(0) with { Runways = [] }).ShouldBeNull();
    }
}

public class AirportFacilityJsonTests
{
    [Fact]
    public async Task The_dump_says_who_captured_it_and_keeps_the_numbers_of_the_simulator()
    {
        var facility = await new MsfsFacilityReader(() => FakeFacilityTransport.SmallAirport(), TimeSpan.FromMilliseconds(100)).ReadAsync("SBXX", TestContext.Current.CancellationToken);
        var capture = new FacilityCapture("fonia 1.2.3", new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero), false);

        var json = JsonNode.Parse(AirportFacilityJson.Write(facility, capture))!;

        json["capture"]!["tool"]!.GetValue<string>().ShouldBe("fonia 1.2.3");
        json["capture"]!["isMsfs2024"]!.GetValue<bool>().ShouldBeFalse();
        json["facility"]!["airport"]!["icao"]!.GetValue<string>().ShouldBe("SBXX");
        json["facility"]!["runways"]![0]!["primaryNumber"]!.GetValue<int>().ShouldBe(11);
        json["facility"]!["frequencies"]![0]!["typeName"]!.GetValue<string>().ShouldBe("TOWER");
        json["facility"]!["frequencies"]![0]!["hertz"]!.GetValue<int>().ShouldBe(118_275_000);
        json["facility"]!["taxiPoints"]!.AsArray().Count.ShouldBe(3);
        json["facility"]!["requests"]!.AsArray().Count.ShouldBe(7);
    }
}
