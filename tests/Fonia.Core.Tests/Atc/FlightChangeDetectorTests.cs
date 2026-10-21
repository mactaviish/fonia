using Fonia.Core.Atc;
using Fonia.Core.Sim;

namespace Fonia.Core.Tests.Atc;

// The simulator loads another flight while the app is running: a different airport, a different aircraft.
public class FlightChangeDetectorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 15, 20, 0, 0, TimeSpan.Zero);

    // Curitiba, SBCT.
    private const double Lat = -25.5285;
    private const double Lon = -49.1758;

    private static AircraftState State(double lat, double lon, int seconds, string registration = "PR-FAB", double knots = 0) => new(
        Noon.AddSeconds(seconds), lat, lon, 3000, 0, knots, 150, true, null, null, 1200, registration, "C172");

    [Fact]
    public void The_first_state_is_the_baseline_and_not_a_new_flight()
    {
        new FlightChangeDetector().Observe(State(Lat, Lon, 0)).ShouldBeNull();
    }

    [Fact]
    public void Moving_normally_is_the_same_flight()
    {
        var detector = new FlightChangeDetector();
        detector.Observe(State(Lat, Lon, 0));

        // 250 knots for a second is about 130 m.
        detector.Observe(State(Lat + 0.0012, Lon, 1, knots: 250)).ShouldBeNull();
        detector.Observe(State(Lat + 0.0024, Lon, 2, knots: 250)).ShouldBeNull();
    }

    [Fact]
    public void A_jump_of_tens_of_kilometers_between_two_readings_is_another_flight()
    {
        var detector = new FlightChangeDetector();
        detector.Observe(State(Lat, Lon, 0));

        // Guarulhos, 359 km away, one second later.
        var change = detector.Observe(State(-23.4356, -46.4731, 1));

        change.ShouldNotBeNull();
        change.State.Registration.ShouldBe("PR-FAB");
        change.Reason.ShouldBe(FlightChangeReason.Teleported);
    }

    [Fact]
    public void After_the_change_the_new_position_is_the_baseline()
    {
        var detector = new FlightChangeDetector();
        detector.Observe(State(Lat, Lon, 0));
        detector.Observe(State(-23.4356, -46.4731, 1)).ShouldNotBeNull();

        detector.Observe(State(-23.4356, -46.4731, 2)).ShouldBeNull();
    }

    [Fact]
    public void Another_aircraft_is_another_flight_even_at_the_same_place()
    {
        var detector = new FlightChangeDetector();
        detector.Observe(State(Lat, Lon, 0, "PR-FAB"));

        var change = detector.Observe(State(Lat, Lon, 1, "PT-ABC"));

        change.ShouldNotBeNull();
        change.Reason.ShouldBe(FlightChangeReason.AnotherAircraft);
    }

    [Theory]
    [InlineData("PR-FAB", "PRFAB")]
    [InlineData("pr fab", "PR-FAB")]
    public void The_same_registration_written_differently_is_the_same_aircraft(string first, string second)
    {
        var detector = new FlightChangeDetector();
        detector.Observe(State(Lat, Lon, 0, first));

        detector.Observe(State(Lat, Lon, 1, second)).ShouldBeNull();
    }

    [Theory]
    [InlineData("", "PR-FAB")]
    [InlineData("PR-FAB", "")]
    [InlineData("", "")]
    public void An_empty_registration_is_a_reading_that_is_not_settled_yet_and_changes_nothing(string first, string second)
    {
        var detector = new FlightChangeDetector();
        detector.Observe(State(Lat, Lon, 0, first));

        detector.Observe(State(Lat, Lon, 1, second)).ShouldBeNull();
    }

    // The simulator publishes nothing while a flight loads: a long silence followed by another place a few kilometers away.
    [Fact]
    public void A_long_silence_and_a_few_kilometers_is_a_flight_loaded_at_a_nearby_place()
    {
        var detector = new FlightChangeDetector();
        detector.Observe(State(Lat, Lon, 0));

        // 0.05 degrees of latitude is about 5.5 km, after 30 seconds without readings.
        var change = detector.Observe(State(Lat + 0.05, Lon, 30));

        change.ShouldNotBeNull();
        change.Reason.ShouldBe(FlightChangeReason.Teleported);
    }

    [Fact]
    public void A_few_kilometers_with_no_silence_is_a_fast_aircraft_and_not_a_new_flight()
    {
        var detector = new FlightChangeDetector();
        detector.Observe(State(Lat, Lon, 0, knots: 400));

        detector.Observe(State(Lat + 0.03, Lon, 2, knots: 400)).ShouldBeNull();
    }

    [Fact]
    public void A_long_silence_with_no_move_is_a_pause_and_the_same_flight()
    {
        var detector = new FlightChangeDetector();
        detector.Observe(State(Lat, Lon, 0));

        detector.Observe(State(Lat, Lon, 600)).ShouldBeNull();
    }

    [Fact]
    public void The_distance_between_two_points_is_the_one_the_airports_use()
    {
        Geo.DistanceKm(Lat, Lon, -23.4356, -46.4731).ShouldBe(359.1, 0.5);
        Geo.DistanceKm(0, 0, 1, 0).ShouldBe(111.2, 0.3);
        Geo.DistanceKm(Lat, Lon, Lat, Lon).ShouldBe(0, 0.001);
    }

    [Fact]
    public void Another_flight_number_on_the_same_aircraft_is_a_new_flight()
    {
        var detector = new FlightChangeDetector();
        var first = State(Lat, Lon, 0) with { Airline = "Gol", FlightNumber = "1234" };

        detector.Observe(first).ShouldBeNull();
        detector.Observe(first with { Timestamp = first.Timestamp.AddSeconds(1), FlightNumber = "1234" }).ShouldBeNull();
        detector.Observe(first with { Timestamp = first.Timestamp.AddSeconds(2), FlightNumber = "5678" }).ShouldNotBeNull();
    }
}
