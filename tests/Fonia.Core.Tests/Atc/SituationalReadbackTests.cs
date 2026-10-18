using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// The readback is judged with what the simulator shows: where the aircraft is, which way it points, whether it moves. An aircraft that already
// does what was cleared has heard it; one lined up on the runway has not misheard the runway.
public class SituationalReadbackTests
{
    private const string Own = "PRFAB";

    // Test airport: runway 11/29, so the end "11" points to 110 degrees magnetic.
    private static readonly AirportContext SoloAndTower = AirportContext.Create(TestAirports.WithSolo());

    private static readonly AirportContext OnTower =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private static readonly AirportContext OnSolo =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency);

    private readonly AtcEngine _engine = new();

    private static AircraftState Aircraft(bool onGround = true, double knots = 0, double heading = 110, double aglFeet = 0, double? latitude = null) => new(
        DateTimeOffset.UtcNow, latitude ?? SoloAndTower.Airport.LatitudeDegrees, SoloAndTower.Airport.LongitudeDegrees, 3000, aglFeet, knots, heading, onGround,
        null, null, 1200, "PR-FAB", "C172");

    private AtcDecision Say(string said, AtcInstruction pending, FlightPhase phase, AirportContext airport, AircraftState? aircraft, bool callsign = true, string runway = "11")
    {
        var flight = new FlightContext(Own, phase, "SBXX", pending, pending, null, runway, aircraft);

        return _engine.Handle(new Readback(callsign ? Own : null, said), flight, airport);
    }

    private static readonly LineUpAndWait LineUp = new("11");
    private static readonly TakeoffClearance Takeoff = new("11");
    private static readonly LandingClearance Landing = new("11");

    // The aircraft already does what was cleared: whatever the readback was, there is nothing to correct.
    [Theory]
    [InlineData(true, 80)]
    [InlineData(false, 90)]
    public void An_aircraft_that_is_taking_off_needs_no_readback_of_the_takeoff_clearance(bool onGround, double knots)
    {
        var decision = Say("blá blá", Takeoff, FlightPhase.TakeoffCleared, OnTower, Aircraft(onGround, knots), callsign: false);

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Fact]
    public void An_aircraft_already_rolling_on_the_runway_needs_no_readback_of_the_line_up()
    {
        Say("ciente", LineUp, FlightPhase.LineUpCleared, OnTower, Aircraft(knots: 60), callsign: false).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void An_aircraft_that_already_landed_needs_no_readback_of_the_landing_clearance()
    {
        Say("blá", Landing, FlightPhase.LandingCleared, OnTower, Aircraft(onGround: true, knots: 40), callsign: false).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void An_aircraft_already_taxiing_needs_no_readback_of_the_taxi_clearance()
    {
        Say("blá", new TaxiClearance("11"), FlightPhase.Taxiing, OnSolo, Aircraft(knots: 8), callsign: false).Instruction.ShouldBeOfType<NoResponse>();
    }

    // Lined up on the runway of the instruction: it was heard.
    [Theory]
    [InlineData("ciente")]
    [InlineData("copiado")]
    [InlineData("pista uno cinco")]
    public void A_pilot_lined_up_on_the_runway_who_gives_the_callsign_has_read_back_the_line_up(string said)
    {
        Say(said, LineUp, FlightPhase.LineUpCleared, OnTower, Aircraft(heading: 112)).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void The_same_words_with_the_aircraft_pointing_elsewhere_are_not_a_readback_of_the_line_up()
    {
        Say("ciente", LineUp, FlightPhase.LineUpCleared, OnTower, Aircraft(heading: 20)).Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }

    [Fact]
    public void The_runway_that_was_misheard_is_forgiven_when_the_aircraft_is_lined_up_on_the_right_one()
    {
        Say("alinhe e mantenha pista uno nove", LineUp, FlightPhase.LineUpCleared, OnTower, Aircraft(heading: 108)).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void The_wrong_runway_is_still_corrected_when_the_aircraft_is_not_lined_up_on_the_right_one()
    {
        Say("alinhe e mantenha pista uno nove", LineUp, FlightPhase.LineUpCleared, OnTower, Aircraft(heading: 20)).Instruction.ShouldBeOfType<RequestReadbackCorrection>();
        Say("alinhe e mantenha pista uno nove", LineUp, FlightPhase.LineUpCleared, OnTower, aircraft: null).Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }

    [Fact]
    public void A_callsign_the_recognizer_lost_is_forgiven_when_the_readback_is_right_and_the_aircraft_is_where_the_clearance_says()
    {
        Say("autorizado decolagem pista uno uno", Takeoff, FlightPhase.TakeoffCleared, OnTower, Aircraft(heading: 109), callsign: false)
            .Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void Without_the_callsign_and_without_the_aircraft_in_place_the_readback_is_still_corrected()
    {
        Say("autorizado decolagem pista uno uno", Takeoff, FlightPhase.TakeoffCleared, OnTower, Aircraft(heading: 20), callsign: false)
            .Instruction.ShouldBeOfType<RequestReadbackCorrection>();
        Say("autorizado decolagem pista uno uno", Takeoff, FlightPhase.TakeoffCleared, OnTower, aircraft: null, callsign: false)
            .Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }

    [Fact]
    public void On_final_near_the_airport_the_pilot_who_gives_the_callsign_has_read_back_the_landing()
    {
        Say("ciente", Landing, FlightPhase.LandingCleared, OnTower, Aircraft(onGround: false, knots: 80, aglFeet: 800)).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void Far_from_the_airport_the_same_words_are_not_a_readback_of_the_landing()
    {
        // About 111 km north of the airport.
        Say("ciente", Landing, FlightPhase.LandingCleared, OnTower, Aircraft(onGround: false, knots: 120, aglFeet: 8000, latitude: SoloAndTower.Airport.LatitudeDegrees + 1))
            .Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }

    // The runway of a taxi cannot be seen in the position of the aircraft: the taxi readback keeps the plain rule.
    [Fact]
    public void A_taxi_readback_with_the_wrong_runway_is_corrected_even_with_the_aircraft_stopped_on_the_ground()
    {
        Say("autorizado táxi pista uno nove", new TaxiClearance("11"), FlightPhase.Taxiing, OnSolo, Aircraft(knots: 0)).Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }

    [Fact]
    public void Without_telemetry_everything_works_as_before()
    {
        Say("alinhe e mantenha, ciente", LineUp, FlightPhase.LineUpCleared, OnTower, aircraft: null).Instruction.ShouldBeOfType<NoResponse>();
        Say("ciente", LineUp, FlightPhase.LineUpCleared, OnTower, aircraft: null).Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }
}
