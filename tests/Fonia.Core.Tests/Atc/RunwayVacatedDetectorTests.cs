using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// The telemetry that shows an aircraft got clear of the runway it landed on, on runway 15/33 of the toy airport: the aircraft is on the
// runway at its threshold and leaves it to the east, where the perpendicular distance grows by about 87 m per 0.001 degree of longitude.
public class RunwayVacatedDetectorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly AirportLayout Toy = TestLayouts.Toy();

    private static AircraftState State(double eastShift, int seconds, bool onGround = true) => new(
        Noon.AddSeconds(seconds), -25.5060, -49.1013 + eastShift, 3000, onGround ? 0 : 50, 20, 150, onGround, null, null, 1200, "PR-ABC", "C172");

    [Fact]
    public void An_aircraft_that_was_on_the_runway_and_is_far_enough_from_it_has_vacated_it_at_that_position()
    {
        var detector = new RunwayVacatedDetector();

        detector.Observe(State(0, 0), Toy, "15").ShouldBeNull();
        detector.Observe(State(0.0006, 10), Toy, "15").ShouldBeNull();

        var vacated = detector.Observe(State(0.0014, 20), Toy, "15").ShouldNotBeNull();

        vacated.At.ShouldBe(Noon.AddSeconds(20));
        vacated.Position.ShouldBe(new GeoPoint(-25.5060, -49.1013 + 0.0014));
    }

    [Fact]
    public void The_other_end_of_the_runway_is_the_same_runway()
    {
        var detector = new RunwayVacatedDetector();
        detector.Observe(State(0, 0), Toy, "33");

        detector.Observe(State(0.0014, 20), Toy, "33").ShouldNotBeNull();
    }

    [Fact]
    public void It_is_reported_once()
    {
        var detector = new RunwayVacatedDetector();
        detector.Observe(State(0, 0), Toy, "15");
        detector.Observe(State(0.0014, 20), Toy, "15").ShouldNotBeNull();

        detector.Observe(State(0.0020, 30), Toy, "15").ShouldBeNull();
    }

    [Fact]
    public void An_aircraft_that_never_was_on_the_runway_has_not_vacated_it()
    {
        new RunwayVacatedDetector().Observe(State(0.0014, 0), Toy, "15").ShouldBeNull();
    }

    [Fact]
    public void Still_close_to_the_runway_is_not_clear_of_it()
    {
        var detector = new RunwayVacatedDetector();
        detector.Observe(State(0, 0), Toy, "15");

        detector.Observe(State(0.0006, 10), Toy, "15").ShouldBeNull();
    }

    [Fact]
    public void An_aircraft_in_the_air_is_not_on_the_runway_and_starts_over()
    {
        var detector = new RunwayVacatedDetector();
        detector.Observe(State(0, 0), Toy, "15");
        detector.Observe(State(0, 5, onGround: false), Toy, "15");

        detector.Observe(State(0.0014, 20), Toy, "15").ShouldBeNull();
    }

    [Fact]
    public void Without_the_map_or_the_runway_or_for_a_runway_the_map_does_not_have_nothing_is_reported()
    {
        var detector = new RunwayVacatedDetector();

        detector.Observe(State(0, 0), null, "15");
        detector.Observe(State(0.0014, 10), null, "15").ShouldBeNull();

        detector.Observe(State(0, 20), Toy, null);
        detector.Observe(State(0.0014, 30), Toy, null).ShouldBeNull();

        detector.Observe(State(0, 40), Toy, "09");
        detector.Observe(State(0.0014, 50), Toy, "09").ShouldBeNull();
    }

    [Fact]
    public void Another_runway_to_watch_starts_over()
    {
        var detector = new RunwayVacatedDetector();
        detector.Observe(State(0, 0), Toy, "15");

        detector.Observe(State(0.0014, 10), Toy, "11").ShouldBeNull();
    }
}
