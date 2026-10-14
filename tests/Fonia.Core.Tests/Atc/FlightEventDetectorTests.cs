using Fonia.Core.Atc;
using Fonia.Core.Sim;

namespace Fonia.Core.Tests.Atc;

public class FlightEventDetectorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static AircraftState State(bool onGround, double knots, int seconds) => new(
        Noon.AddSeconds(seconds), -25.5, -49.1, 3000, onGround ? 0 : 50, knots, 150, onGround, null, null, 1200, "PR-ABC", "C172");

    [Fact]
    public void Leaving_the_ground_and_staying_off_it_is_a_takeoff_at_the_time_it_left()
    {
        var detector = new FlightEventDetector();

        detector.Observe(State(true, 80, 20)).ShouldBeNull();
        detector.Observe(State(false, 85, 26)).ShouldBeNull();
        detector.Observe(State(false, 90, 30)).ShouldBeNull();
        detector.Observe(State(false, 95, 31)).ShouldBe(new TookOff(Noon.AddSeconds(26)));
    }

    [Fact]
    public void A_helicopter_lifting_off_at_zero_knots_takes_off_too()
    {
        var detector = new FlightEventDetector();

        detector.Observe(State(true, 0, 0)).ShouldBeNull();
        detector.Observe(State(false, 0, 5)).ShouldBeNull();
        detector.Observe(State(false, 0, 10)).ShouldBe(new TookOff(Noon.AddSeconds(5)));
    }

    [Fact]
    public void The_takeoff_is_announced_once()
    {
        var detector = new FlightEventDetector();
        detector.Observe(State(true, 80, 0));
        detector.Observe(State(false, 85, 1));
        detector.Observe(State(false, 90, 6)).ShouldBeOfType<TookOff>();

        detector.Observe(State(false, 95, 7)).ShouldBeNull();
        detector.Observe(State(false, 99, 60)).ShouldBeNull();
    }

    [Fact]
    public void An_aircraft_that_is_already_flying_when_the_app_starts_did_not_take_off()
    {
        var detector = new FlightEventDetector();

        detector.Observe(State(false, 120, 0)).ShouldBeNull();
        detector.Observe(State(false, 121, 10)).ShouldBeNull();
    }

    [Fact]
    public void A_bounce_that_touches_down_again_within_a_moment_is_not_a_takeoff()
    {
        var detector = new FlightEventDetector();
        detector.Observe(State(true, 60, 0));
        detector.Observe(State(false, 62, 1)).ShouldBeNull();
        detector.Observe(State(false, 62, 4)).ShouldBeNull();
        detector.Observe(State(true, 55, 5)).ShouldBeNull();

        // The clock of the next lift-off starts again from its own first snapshot.
        detector.Observe(State(false, 70, 10)).ShouldBeNull();
        detector.Observe(State(false, 75, 13)).ShouldBeNull();
        detector.Observe(State(false, 80, 15)).ShouldBe(new TookOff(Noon.AddSeconds(10)));
    }

    [Fact]
    public void Each_takeoff_is_announced_after_the_aircraft_lands_in_between()
    {
        var detector = new FlightEventDetector();
        detector.Observe(State(true, 80, 0));
        detector.Observe(State(false, 85, 1));
        detector.Observe(State(false, 85, 6)).ShouldBeOfType<TookOff>();
        detector.Observe(State(true, 60, 100));

        detector.Observe(State(false, 85, 110));
        detector.Observe(State(false, 85, 115)).ShouldBeOfType<TookOff>();
    }
}
