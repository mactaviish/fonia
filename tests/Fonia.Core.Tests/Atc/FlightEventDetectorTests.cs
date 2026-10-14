using Fonia.Core.Atc;
using Fonia.Core.Sim;

namespace Fonia.Core.Tests.Atc;

public class FlightEventDetectorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static AircraftState State(bool onGround, double knots, int seconds = 0) => new(
        Noon.AddSeconds(seconds), -25.5, -49.1, 3000, onGround ? 0 : 50, knots, 150, onGround, null, null, 1200, "PR-ABC", "C172");

    [Fact]
    public void Leaving_the_ground_at_speed_is_a_takeoff_at_the_time_of_that_snapshot()
    {
        var detector = new FlightEventDetector();

        detector.Observe(State(true, 0)).ShouldBeNull();
        detector.Observe(State(true, 80, 20)).ShouldBeNull();
        detector.Observe(State(false, 85, 26)).ShouldBe(new TookOff(Noon.AddSeconds(26)));
    }

    [Fact]
    public void The_takeoff_is_announced_once()
    {
        var detector = new FlightEventDetector();
        detector.Observe(State(true, 80));
        detector.Observe(State(false, 85));

        detector.Observe(State(false, 90)).ShouldBeNull();
        detector.Observe(State(false, 95)).ShouldBeNull();
    }

    [Fact]
    public void An_aircraft_that_is_already_flying_when_the_app_starts_did_not_take_off()
    {
        var detector = new FlightEventDetector();

        detector.Observe(State(false, 120)).ShouldBeNull();
        detector.Observe(State(false, 121)).ShouldBeNull();
    }

    [Fact]
    public void A_hop_at_taxi_speed_is_not_a_takeoff()
    {
        var detector = new FlightEventDetector();
        detector.Observe(State(true, 5));

        detector.Observe(State(false, 8)).ShouldBeNull();
    }

    [Fact]
    public void Each_takeoff_is_announced_after_the_aircraft_lands_in_between()
    {
        var detector = new FlightEventDetector();
        detector.Observe(State(true, 80));
        detector.Observe(State(false, 85)).ShouldBeOfType<TookOff>();
        detector.Observe(State(true, 60));

        detector.Observe(State(true, 80));
        detector.Observe(State(false, 85)).ShouldBeOfType<TookOff>();
    }
}
