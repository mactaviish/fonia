using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// The Torre's part of a departure: line up and wait, the takeoff clearance and what is read back (MCA 100-16/2025, Art. 126).
public class TowerDepartureTests
{
    private const string Own = "PRABC";

    private readonly AtcEngine _engine = new();

    private static readonly AirportContext SoloAndTower = AirportContext.Create(TestAirports.WithSolo());

    private static Frequency TowerFrequency => SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency;

    private static readonly AirportContext OnTower = FrequencyResolver.Apply(SoloAndTower, TowerFrequency);

    private static readonly FlightContext Handed = new(Own, FlightPhase.HandedToTower, "SBXX", null, null, null, "11");

    private static AtcDecision Handle(Intent intent, FlightContext flight, AirportContext? airport = null) =>
        new AtcEngine().Handle(intent, flight, airport ?? OnTower);

    [Fact]
    public void The_pilot_ready_for_departure_is_told_to_line_up_and_wait_on_the_runway_of_the_taxi()
    {
        var decision = Handle(new RequestDeparture(Own, "11"), Handed);

        decision.Instruction.ShouldBe(new LineUpAndWait("11"));
        decision.NextContext.Phase.ShouldBe(FlightPhase.LineUpCleared);
        decision.NextContext.PendingReadback.ShouldBe(new LineUpAndWait("11"));
        decision.NextContext.LastInstruction.ShouldBe(new LineUpAndWait("11"));
    }

    [Fact]
    public void The_runway_is_the_one_assigned_at_the_taxi_not_the_one_in_use_when_they_differ()
    {
        var flight = Handed with { AssignedRunway = "29" };

        Handle(new RequestDeparture(Own, null), flight).Instruction.ShouldBe(new LineUpAndWait("29"));
    }

    [Fact]
    public void Without_a_runway_assigned_the_runway_in_use_is_taken()
    {
        var flight = Handed with { AssignedRunway = null };

        Handle(new RequestDeparture(Own, null), flight).Instruction.ShouldBe(new LineUpAndWait(OnTower.RunwayInUse!));
    }

    [Fact]
    public void Reporting_the_holding_point_on_the_torre_is_the_same_as_asking_for_departure()
    {
        Handle(new ReportHoldingShort(Own, "11"), Handed).Instruction.ShouldBe(new LineUpAndWait("11"));
    }

    [Fact]
    public void The_taxi_clearance_leaves_its_runway_for_the_torre()
    {
        var parked = new FlightContext(Own, FlightPhase.StartupApproved, "SBXX", null);
        var cleared = _engine.Handle(new RequestTaxi(Own, null), parked, SoloAndTower).NextContext;

        cleared.AssignedRunway.ShouldBe("11");
    }

    // ---- line-up readback

    [Theory]
    [InlineData("alinhe e mantenha pista uno uno")]
    [InlineData("alinhando e mantendo pista uno uno")]
    [InlineData("pista uno uno alinhe e mantenha")]
    public void A_correct_line_up_readback_with_the_callsign_gets_no_answer(string said)
    {
        var lineUp = Handle(new RequestDeparture(Own, null), Handed).NextContext;

        var decision = Handle(new Readback(Own, said), lineUp);

        decision.Instruction.ShouldBe(new NoResponse());
        decision.NextContext.PendingReadback.ShouldBeNull();
        decision.NextContext.Phase.ShouldBe(FlightPhase.LineUpCleared);
    }

    [Theory]
    [InlineData("alinhe e mantenha pista uno cinco", "wrong runway")]
    [InlineData("pista uno uno", "no instruction")]
    [InlineData("ciente", "nothing")]
    public void A_wrong_or_incomplete_line_up_readback_is_corrected_and_stays_pending(string said, string why)
    {
        var lineUp = Handle(new RequestDeparture(Own, null), Handed).NextContext;

        var decision = Handle(new Readback(Own, said), lineUp);

        decision.Instruction.ShouldBe(new RequestReadbackCorrection(new LineUpAndWait("11")), why);
        decision.NextContext.PendingReadback.ShouldBe(new LineUpAndWait("11"));
    }

    [Fact]
    public void A_line_up_readback_without_the_callsign_is_corrected()
    {
        var lineUp = Handle(new RequestDeparture(Own, null), Handed).NextContext;

        Handle(new Readback(null, "alinhe e mantenha pista uno uno"), lineUp).Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }

    // ---- takeoff clearance

    [Fact]
    public void Lined_up_the_pilot_is_cleared_for_takeoff()
    {
        var lineUp = Handle(new RequestDeparture(Own, null), Handed).NextContext;

        var decision = Handle(new ReportLinedUp(Own, null), lineUp);

        decision.Instruction.ShouldBe(new TakeoffClearance("11"));
        decision.NextContext.Phase.ShouldBe(FlightPhase.TakeoffCleared);
        decision.NextContext.PendingReadback.ShouldBe(new TakeoffClearance("11"));
    }

    [Fact]
    public void Reporting_lined_up_also_counts_as_the_readback_of_the_line_up_that_was_waiting()
    {
        var lineUp = Handle(new RequestDeparture(Own, null), Handed).NextContext;
        lineUp.PendingReadback.ShouldBe(new LineUpAndWait("11"));

        Handle(new ReportLinedUp(Own, null), lineUp).NextContext.PendingReadback.ShouldBe(new TakeoffClearance("11"));
    }

    [Fact]
    public void Ready_for_departure_again_after_the_line_up_is_cleared_for_takeoff()
    {
        var lineUp = Handle(new RequestDeparture(Own, null), Handed).NextContext;

        Handle(new RequestDeparture(Own, null), lineUp).Instruction.ShouldBe(new TakeoffClearance("11"));
    }

    [Fact]
    public void A_pilot_who_says_lined_up_without_having_been_told_to_is_cleared_for_takeoff()
    {
        Handle(new ReportLinedUp(Own, null), Handed).Instruction.ShouldBe(new TakeoffClearance("11"));
    }

    [Theory]
    [InlineData("autorizado decolagem pista uno uno")]
    [InlineData("autorizado decolar pista uno uno")]
    [InlineData("decolagem pista uno uno")]
    public void A_correct_takeoff_readback_gets_no_answer(string said)
    {
        var cleared = Handle(new ReportLinedUp(Own, null), Handed).NextContext;

        var decision = Handle(new Readback(Own, said), cleared);

        decision.Instruction.ShouldBe(new NoResponse());
        decision.NextContext.PendingReadback.ShouldBeNull();
        decision.NextContext.Phase.ShouldBe(FlightPhase.TakeoffCleared);
    }

    [Theory]
    [InlineData("autorizado decolagem pista uno cinco")]
    [InlineData("pista uno uno")]
    [InlineData("ciente")]
    public void A_wrong_takeoff_readback_is_corrected_and_stays_pending(string said)
    {
        var cleared = Handle(new ReportLinedUp(Own, null), Handed).NextContext;

        var decision = Handle(new Readback(Own, said), cleared);

        decision.Instruction.ShouldBe(new RequestReadbackCorrection(new TakeoffClearance("11")));
        decision.NextContext.PendingReadback.ShouldBe(new TakeoffClearance("11"));
    }

    [Fact]
    public void The_pilot_who_starts_the_takeoff_roll_without_reading_back_is_taken_in_silence()
    {
        var cleared = Handle(new ReportLinedUp(Own, null), Handed).NextContext;

        var decision = Handle(new ReportTakingOff(Own), cleared);

        decision.Instruction.ShouldBe(new NoResponse());
        decision.NextContext.PendingReadback.ShouldBeNull();
        decision.NextContext.Phase.ShouldBe(FlightPhase.TakeoffCleared);
    }

    [Fact]
    public void The_wind_goes_into_the_clearance_when_there_is_one()
    {
        var withWind = OnTower with { Wind = new Wind(95, 10) };

        Handle(new ReportLinedUp(Own, null), Handed, withWind).Instruction.ShouldBe(new TakeoffClearance("11", new Wind(95, 10)));
    }

    [Fact]
    public void Without_a_wind_the_clearance_has_none_and_is_not_made_up()
    {
        Handle(new ReportLinedUp(Own, null), Handed).Instruction.ShouldBe(new TakeoffClearance("11", null));
    }

    [Fact]
    public void Asking_to_say_it_again_repeats_the_last_instruction()
    {
        var lineUp = Handle(new RequestDeparture(Own, null), Handed).NextContext;

        Handle(new SayAgain(Own), lineUp).Instruction.ShouldBe(new LineUpAndWait("11"));
    }

    [Fact]
    public void Asking_for_departure_after_the_clearance_repeats_it_instead_of_giving_another()
    {
        var cleared = Handle(new ReportLinedUp(Own, null), Handed).NextContext;

        Handle(new RequestDeparture(Own, null), cleared).Instruction.ShouldBe(new TakeoffClearance("11"));
    }

    // ---- who may say what

    [Theory]
    [InlineData(FlightPhase.Parked)]
    [InlineData(FlightPhase.PushbackApproved)]
    [InlineData(FlightPhase.StartupApproved)]
    public void Before_the_taxi_a_departure_makes_no_sense_and_changes_nothing(FlightPhase phase)
    {
        var flight = Handed with { Phase = phase };

        var decision = Handle(new RequestDeparture(Own, null), flight);

        decision.Instruction.ShouldBe(new SayAgainRequest());
        decision.NextContext.ShouldBe(flight);
    }

    [Fact]
    public void An_airport_without_a_usable_runway_cannot_clear_a_departure()
    {
        var noRunway = OnTower with { RunwayInUse = null };
        var flight = Handed with { AssignedRunway = null };

        Handle(new RequestDeparture(Own, null), flight, noRunway).Instruction.ShouldBe(new NoRunwayAvailable());
    }

    [Fact]
    public void Without_the_callsign_the_request_is_held_until_the_pilot_says_it()
    {
        var asked = Handle(new RequestDeparture(null, null), Handed);

        asked.Instruction.ShouldBe(new RequestCallsign());

        Handle(new CallsignOnly(Own), asked.NextContext).Instruction.ShouldBe(new LineUpAndWait("11"));
    }

    [Fact]
    public void The_torre_does_not_do_the_solos_job_when_the_airport_has_a_solo_and_says_where_to_call()
    {
        var ground = SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency;
        var parked = new FlightContext(Own, FlightPhase.Parked, "SBXX", null);

        Handle(new RequestStartup(Own, null), parked).Instruction.ShouldBe(new CallTheSolo(ground));
        Handle(new RequestPushback(Own, false, null), parked).Instruction.ShouldBe(new CallTheSolo(ground));
        Handle(new RequestTaxi(Own, null), parked).Instruction.ShouldBe(new CallTheSolo(ground));
    }

    [Theory]
    [InlineData("departure")]
    [InlineData("linedup")]
    [InlineData("takingoff")]
    public void The_solo_does_not_clear_departures_it_passes_the_flight_to_the_torre(string kind)
    {
        Intent intent = kind switch
        {
            "departure" => new RequestDeparture(Own, null),
            "linedup" => new ReportLinedUp(Own, null),
            _ => new ReportTakingOff(Own),
        };
        var taxiing = new FlightContext(Own, FlightPhase.Taxiing, "SBXX", null);
        var onSolo = FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency);

        var decision = Handle(intent, taxiing, onSolo);

        decision.Instruction.ShouldBe(new HandoffToTower(TowerFrequency));
        decision.NextContext.Phase.ShouldBe(FlightPhase.HandedToTower);
    }

    [Fact]
    public void An_airport_with_only_a_torre_does_the_whole_departure_after_the_taxi()
    {
        var towerOnly = AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)], AirportFrequencyType.Tower));
        var taxiing = new FlightContext(Own, FlightPhase.Taxiing, "SBXX", null, null, null, "11");

        _engine.Handle(new RequestDeparture(Own, null), taxiing, towerOnly).Instruction.ShouldBe(new LineUpAndWait("11"));
    }
}
