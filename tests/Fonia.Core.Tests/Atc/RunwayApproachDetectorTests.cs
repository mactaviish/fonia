using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

public class RunwayApproachDetectorTests
{
    private static readonly AirportLayout Toy = TestLayouts.Toy();

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static AircraftState At(int node, int seconds, bool onGround = true)
    {
        var position = Toy.Nodes.Single(n => n.Id == node).Position;

        return new AircraftState(Start.AddSeconds(seconds), position.LatitudeDegrees, position.LongitudeDegrees, 3000, onGround ? 0 : 500, 5, 150, onGround, null, null, 1200, "PR-ABC", "C172");
    }

    [Fact]
    public void Close_to_a_holding_point_it_reports_and_repeats_every_few_seconds_while_the_aircraft_stays()
    {
        var detector = new RunwayApproachDetector();

        detector.Observe(At(5, 0), Toy, "15").ShouldNotBeNull();
        detector.Observe(At(5, 2), Toy, "15").ShouldBeNull();
        detector.Observe(At(5, 5), Toy, "15").ShouldNotBeNull();
    }

    [Fact]
    public void Far_from_every_holding_point_and_the_runway_it_does_not_report()
    {
        new RunwayApproachDetector().Observe(At(1, 0), Toy, "15").ShouldBeNull();
    }

    [Fact]
    public void Leaving_and_coming_back_reports_at_once()
    {
        var detector = new RunwayApproachDetector();

        detector.Observe(At(5, 0), Toy, "15").ShouldNotBeNull();
        detector.Observe(At(1, 1), Toy, "15").ShouldBeNull();
        detector.Observe(At(5, 2), Toy, "15").ShouldNotBeNull();
    }

    [Fact]
    public void On_the_departure_runway_it_reports_even_far_from_a_holding_point()
    {
        new RunwayApproachDetector().Observe(At(9, 0), Toy, "15").ShouldNotBeNull();
        new RunwayApproachDetector().Observe(At(9, 0), Toy, null).ShouldBeNull();
    }

    [Fact]
    public void Without_the_map_or_on_the_air_it_never_reports()
    {
        var detector = new RunwayApproachDetector();

        detector.Observe(At(5, 0), null, "15").ShouldBeNull();
        detector.Observe(At(5, 10, onGround: false), Toy, "15").ShouldBeNull();
    }

    [Fact]
    public void The_event_carries_where_the_aircraft_is()
    {
        var found = new RunwayApproachDetector().Observe(At(5, 0), Toy, "15");

        found!.Position.ShouldBe(Toy.Nodes.Single(n => n.Id == 5).Position);
    }
}
