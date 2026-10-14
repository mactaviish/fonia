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

    [Fact]
    public void Touching_down_and_staying_on_the_ground_is_a_landing_at_the_time_of_the_touchdown()
    {
        var detector = new FlightEventDetector();
        detector.Observe(State(true, 0, 0));
        detector.Observe(State(false, 90, 1));
        detector.Observe(State(false, 90, 7)).ShouldBeOfType<TookOff>();
        detector.Observe(State(false, 80, 600));

        detector.Observe(State(true, 60, 610)).ShouldBeNull();
        detector.Observe(State(true, 40, 613)).ShouldBeNull();
        detector.Observe(State(true, 20, 616)).ShouldBe(new TouchedDown(Noon.AddSeconds(610)));
    }

    [Fact]
    public void A_bounce_on_landing_is_one_landing_and_not_two_or_none()
    {
        var detector = new FlightEventDetector();
        detector.Observe(State(false, 80, 0));
        detector.Observe(State(false, 80, 10));

        detector.Observe(State(true, 60, 20)).ShouldBeNull();
        detector.Observe(State(false, 55, 22)).ShouldBeNull();
        detector.Observe(State(true, 50, 24)).ShouldBeNull();
        detector.Observe(State(true, 40, 30)).ShouldBe(new TouchedDown(Noon.AddSeconds(24)));
        detector.Observe(State(true, 10, 40)).ShouldBeNull();
    }

    [Fact]
    public void An_aircraft_already_flying_when_the_app_starts_lands_like_any_other()
    {
        var detector = new FlightEventDetector();
        detector.Observe(State(false, 120, 0));
        detector.Observe(State(false, 120, 8));
        detector.Observe(State(true, 60, 100));

        detector.Observe(State(true, 30, 106)).ShouldBe(new TouchedDown(Noon.AddSeconds(100)));
    }

    [Fact]
    public void An_aircraft_that_only_taxis_never_lands()
    {
        var detector = new FlightEventDetector();

        foreach (var second in Enumerable.Range(0, 60))
        {
            detector.Observe(State(true, 10, second)).ShouldBeNull();
        }
    }

    [Fact]
    public void A_touch_and_go_is_not_a_landing_but_the_takeoff_that_follows_is_announced()
    {
        var detector = new FlightEventDetector();
        detector.Observe(State(false, 80, 0));
        detector.Observe(State(false, 80, 10));
        detector.Observe(State(true, 60, 20)).ShouldBeNull();

        detector.Observe(State(false, 70, 23)).ShouldBeNull();
        detector.Observe(State(false, 80, 30)).ShouldBe(new TookOff(Noon.AddSeconds(23)));
    }
}
