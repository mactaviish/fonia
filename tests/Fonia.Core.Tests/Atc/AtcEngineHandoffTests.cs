using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

public class AtcEngineHandoffTests
{
    private const string Own = "PRABC";

    private readonly AtcEngine _engine = new();

    private static readonly AirportContext SoloAndTower = AirportContext.Create(TestAirports.WithSolo());

    private static readonly FlightContext Taxiing = new(Own, FlightPhase.Taxiing, "SBXX", new TaxiClearance("11"), new TaxiClearance("11"));

    private static readonly FlightContext Parked = new(Own, FlightPhase.Parked, "SBXX", null);

    private static Frequency Tower => SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency;

    [Fact]
    public void At_the_holding_point_the_solo_passes_the_flight_to_the_torre_with_its_frequency()
    {
        var decision = _engine.Handle(new ReportHoldingShort(Own, "11"), Taxiing, SoloAndTower);

        decision.Instruction.ShouldBe(new HandoffToTower(Tower));
        decision.NextContext.Phase.ShouldBe(FlightPhase.HandedToTower);
        decision.NextContext.LastInstruction.ShouldBe(new HandoffToTower(Tower));
    }

    [Fact]
    public void The_handoff_takes_the_place_of_the_taxi_readback_that_was_still_waiting()
    {
        var decision = _engine.Handle(new ReportHoldingShort(Own, "11"), Taxiing, SoloAndTower);

        // What the pilot says next is an answer to the handoff, not to the taxi.
        decision.NextContext.PendingReadback.ShouldBe(new HandoffToTower(Tower));
    }

    // The whole join, as in the live test: what the engine leaves behind is what the NLU reads the pilot's next words with.
    [Theory]
    [InlineData("Chamará Torre Curitiba no...")]
    [InlineData("Uno noito, decimal cinco, cinco, Alfa Bravo Charlie.")]
    [InlineData("1, 1, 8, decimal cinco para o Alfa Bravo Charlie.")]
    [InlineData("uno uno nove decimal cinco, Papa Romeo Alfa Bravo Charlie")]
    public void What_the_pilot_says_after_the_handoff_is_taken_in_silence_and_never_asked_to_be_repeated(string said)
    {
        var handed = _engine.Handle(new ReportHoldingShort(Own, "11"), Taxiing, SoloAndTower).NextContext;

        var decision = _engine.Handle(new RuleBasedNlu().Interpret(said, handed), handed, SoloAndTower);

        decision.Instruction.ShouldBe(new NoResponse());
        decision.NextContext.PendingReadback.ShouldBeNull();
        decision.NextContext.Phase.ShouldBe(FlightPhase.HandedToTower);
    }

    // The manual does not list the frequency change among what must be read back (Art. 12), but pilots do it: the Solo takes
    // it in silence, with or without the callsign, and does not argue about how it was said.
    [Theory]
    [InlineData("chamarei Torre Curitiba uno uno oito decimal cinco cinco")]
    [InlineData("Chamará Torre Curitiba no")]
    [InlineData("uno oito decimal cinco para o Alfa Bravo Charlie")]
    [InlineData("uno uno nove decimal cinco")]
    public void A_readback_of_the_handoff_is_taken_in_silence_whatever_was_said(string said)
    {
        var handed = _engine.Handle(new ReportHoldingShort(Own, "11"), Taxiing, SoloAndTower).NextContext;

        var decision = _engine.Handle(new Readback(null, said), handed, SoloAndTower);

        decision.Instruction.ShouldBe(new NoResponse());
        decision.NextContext.PendingReadback.ShouldBeNull();
        decision.NextContext.Phase.ShouldBe(FlightPhase.HandedToTower);
    }

    [Fact]
    public void Not_reading_the_handoff_back_is_not_a_problem()
    {
        var handed = _engine.Handle(new ReportHoldingShort(Own, "11"), Taxiing, SoloAndTower).NextContext;

        // The pilot goes on with something else: nothing is demanded.
        _engine.Handle(new SayAgain(Own), handed, SoloAndTower).Instruction.ShouldBe(new HandoffToTower(Tower));
        _engine.Handle(new CallsignOnly(Own), handed, SoloAndTower).Instruction.ShouldBe(new GoAhead());
    }

    [Fact]
    public void Asking_the_solo_to_say_it_again_repeats_the_handoff()
    {
        var handed = _engine.Handle(new ReportHoldingShort(Own, "11"), Taxiing, SoloAndTower).NextContext;

        _engine.Handle(new SayAgain(Own), handed, SoloAndTower).Instruction.ShouldBe(new HandoffToTower(Tower));
    }

    [Theory]
    [InlineData(FlightPhase.Parked)]
    [InlineData(FlightPhase.PushbackApproved)]
    [InlineData(FlightPhase.StartupApproved)]
    public void A_report_before_the_taxi_makes_no_sense_and_changes_nothing(FlightPhase phase)
    {
        var flight = Parked with { Phase = phase };

        var decision = _engine.Handle(new ReportHoldingShort(Own, "11"), flight, SoloAndTower);

        decision.Instruction.ShouldBe(new SayAgainRequest());
        decision.NextContext.ShouldBe(flight);
    }

    [Fact]
    public void An_airport_without_a_torre_has_nobody_to_pass_the_flight_to()
    {
        var soloOnly = AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)], AirportFrequencyType.Ground));

        var decision = _engine.Handle(new ReportHoldingShort(Own, "11"), Taxiing, soloOnly);

        decision.Instruction.ShouldBe(new NoTowerAvailable());
        decision.NextContext.Phase.ShouldBe(FlightPhase.Taxiing);
    }

    [Fact]
    public void Without_the_callsign_the_report_is_held_until_the_pilot_says_it_and_then_goes_ahead()
    {
        var asked = _engine.Handle(new ReportHoldingShort(null, "11"), Taxiing, SoloAndTower);

        asked.Instruction.ShouldBe(new RequestCallsign());

        var answered = _engine.Handle(new CallsignOnly(Own), asked.NextContext, SoloAndTower);

        answered.Instruction.ShouldBe(new HandoffToTower(Tower));
    }

    [Fact]
    public void On_a_frequency_nobody_answers_the_tuned_frequency_is_in_the_message()
    {
        var elsewhere = FrequencyResolver.Apply(SoloAndTower, new Frequency(121500));

        var decision = _engine.Handle(new RequestStartup(Own, null), Parked, elsewhere);

        decision.Instruction.ShouldBe(new NoOneOnFrequency(new Frequency(121500)));
    }

    [Fact]
    public void On_the_torre_of_an_airport_that_has_a_solo_the_torre_does_not_do_the_solos_job()
    {
        var onTower = FrequencyResolver.Apply(SoloAndTower, Tower);
        var solo = SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency;

        _engine.Handle(new RequestStartup(Own, null), Parked, onTower).Instruction.ShouldBe(new CallTheSolo(solo));
        _engine.Handle(new RequestPushback(Own, false, null), Parked, onTower).Instruction.ShouldBe(new CallTheSolo(solo));
        _engine.Handle(new RequestTaxi(Own, null), Parked, onTower).Instruction.ShouldBe(new CallTheSolo(solo));
    }

    [Fact]
    public void On_the_torre_the_report_of_the_holding_point_is_a_departure_the_runway_free_is_cleared_and_busy_is_lined_up()
    {
        var onTower = FrequencyResolver.Apply(SoloAndTower, Tower);

        _engine.Handle(new ReportHoldingShort(Own, "11"), Taxiing, onTower).Instruction.ShouldBeOfType<TakeoffClearance>();
        _engine.Handle(new ReportHoldingShort(Own, "11"), Taxiing, onTower with { RunwayBusy = true }).Instruction.ShouldBeOfType<LineUpAndWait>();
    }

    [Fact]
    public void An_airport_with_only_a_torre_keeps_doing_the_solos_job_as_before()
    {
        var towerOnly = AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)], AirportFrequencyType.Tower));

        _engine.Handle(new RequestStartup(Own, null), Parked, towerOnly).Instruction.ShouldBe(new StartupApproved());
    }

    [Fact]
    public void On_the_torre_what_the_solo_asked_to_be_read_back_is_still_checked()
    {
        var onTower = FrequencyResolver.Apply(SoloAndTower, Tower);

        var decision = _engine.Handle(new Readback(Own, "autorizado taxi pista uno uno"), Taxiing, onTower);

        decision.Instruction.ShouldBe(new NoResponse());
    }

    [Fact]
    public void A_readback_that_is_silently_taken_does_not_stop_a_real_correction_of_the_taxi()
    {
        // Only the handoff is optional: the taxi clearance still has to be read back right.
        var decision = _engine.Handle(new Readback(Own, "autorizado taxi pista uno cinco"), Taxiing, SoloAndTower);

        decision.Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }
}
