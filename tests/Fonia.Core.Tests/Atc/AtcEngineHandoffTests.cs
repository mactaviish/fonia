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
    public void The_handoff_clears_the_taxi_readback_that_was_still_waiting()
    {
        var decision = _engine.Handle(new ReportHoldingShort(Own, "11"), Taxiing, SoloAndTower);

        decision.NextContext.PendingReadback.ShouldBeNull();
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

        _engine.Handle(new RequestStartup(Own, null), Parked, onTower).Instruction.ShouldBe(new TowerPhraseologyPending());
        _engine.Handle(new RequestPushback(Own, false, null), Parked, onTower).Instruction.ShouldBe(new TowerPhraseologyPending());
        _engine.Handle(new RequestTaxi(Own, null), Parked, onTower).Instruction.ShouldBe(new TowerPhraseologyPending());
        _engine.Handle(new ReportHoldingShort(Own, "11"), Taxiing, onTower).Instruction.ShouldBe(new TowerPhraseologyPending());
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

        var decision = _engine.Handle(new Readback(Own, "autorizado taxi pista um um"), Taxiing, onTower);

        decision.Instruction.ShouldBe(new NoResponse());
    }
}
