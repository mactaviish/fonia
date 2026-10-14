using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// The Torre's part of an arrival: circuit, base, final, landing clearance (MCA 100-16/2025, Art. 128 to 132).
public class TowerArrivalTests
{
    private const string Own = "PRABC";

    private static readonly AirportContext SoloAndTower = AirportContext.Create(TestAirports.WithSolo());

    private static readonly AirportContext OnTower =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private static readonly AirportContext OnSolo =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency);

    private static readonly FlightContext Flying = new(Own, FlightPhase.Airborne, "SBXX", null);

    private static readonly string Runway = OnTower.RunwayInUse!;

    private static AtcDecision Handle(Intent intent, FlightContext flight, AirportContext? airport = null) =>
        new AtcEngine().Handle(intent, flight, airport ?? OnTower);

    private static Readback Said(string text) => new(Own, string.Join(' ', SpokenTokens.Tokenize(text)));

    [Fact]
    public void The_pilot_asking_to_land_is_cleared_to_the_circuit_on_the_runway_in_use()
    {
        var decision = Handle(new RequestLanding(Own), Flying);

        decision.Instruction.ShouldBe(new CircuitClearance(Runway));
        decision.NextContext.Phase.ShouldBe(FlightPhase.InCircuit);
        decision.NextContext.PendingReadback.ShouldBe(new CircuitClearance(Runway));
        decision.NextContext.LastInstruction.ShouldBe(new CircuitClearance(Runway));
        decision.NextContext.AssignedRunway.ShouldBe(Runway);
    }

    [Fact]
    public void A_flight_that_was_never_on_the_ground_here_can_still_ask_to_land()
    {
        Handle(new RequestLanding(Own), new FlightContext(Own, FlightPhase.Parked, "SBXX", null)).Instruction.ShouldBe(new CircuitClearance(Runway));
    }

    [Fact]
    public void The_wind_goes_into_the_circuit_clearance_when_there_is_one()
    {
        var windy = OnTower with { Wind = new Wind(110, 12) };

        Handle(new RequestLanding(Own), Flying, windy).Instruction.ShouldBe(new CircuitClearance(Runway, new Wind(110, 12)));
    }

    [Fact]
    public void On_the_downwind_the_pilot_is_told_to_report_the_base_leg()
    {
        var decision = Handle(new ReportDownwind(Own), Flying with { Phase = FlightPhase.InCircuit });

        decision.Instruction.ShouldBeOfType<ReportBase>();
        decision.NextContext.Phase.ShouldBe(FlightPhase.OnDownwind);
    }

    [Fact]
    public void On_the_base_leg_the_pilot_is_told_to_report_final()
    {
        var decision = Handle(new ReportBaseLeg(Own), Flying with { Phase = FlightPhase.OnDownwind });

        decision.Instruction.ShouldBeOfType<ReportOnFinal>();
        decision.NextContext.Phase.ShouldBe(FlightPhase.OnBase);
    }

    [Fact]
    public void On_final_the_pilot_is_cleared_to_land_on_the_runway_of_the_circuit()
    {
        var flight = Flying with { Phase = FlightPhase.OnBase, AssignedRunway = "29" };

        var decision = Handle(new ReportFinal(Own), flight);

        decision.Instruction.ShouldBe(new LandingClearance("29"));
        decision.NextContext.Phase.ShouldBe(FlightPhase.LandingCleared);
        decision.NextContext.PendingReadback.ShouldBe(new LandingClearance("29"));
        decision.NextContext.LastInstruction.ShouldBe(new LandingClearance("29"));
    }

    [Fact]
    public void The_wind_goes_into_the_landing_clearance_when_there_is_one()
    {
        var windy = OnTower with { Wind = new Wind(120, 10) };

        Handle(new ReportFinal(Own), Flying with { Phase = FlightPhase.OnBase, AssignedRunway = "11" }, windy)
            .Instruction.ShouldBe(new LandingClearance("11", new Wind(120, 10)));
    }

    // The pilot says "final" without the controller having sent them round: what they need is the clearance.
    [Theory]
    [InlineData(FlightPhase.Parked)]
    [InlineData(FlightPhase.Airborne)]
    [InlineData(FlightPhase.InCircuit)]
    [InlineData(FlightPhase.OnDownwind)]
    public void A_pilot_who_skips_a_step_is_given_the_clearance_they_need_now(FlightPhase phase)
    {
        Handle(new ReportFinal(Own), Flying with { Phase = phase, AssignedRunway = null }).Instruction.ShouldBe(new LandingClearance(Runway));
    }

    [Fact]
    public void A_phase_never_goes_backwards()
    {
        var cleared = Flying with { Phase = FlightPhase.LandingCleared, AssignedRunway = Runway };

        Handle(new ReportDownwind(Own), cleared).NextContext.Phase.ShouldBe(FlightPhase.LandingCleared);
    }

    [Fact]
    public void Going_around_is_taken_in_and_the_flight_flies_the_circuit_again()
    {
        var decision = Handle(new ReportGoingAround(Own), Flying with { Phase = FlightPhase.LandingCleared, PendingReadback = new LandingClearance(Runway) });

        decision.Instruction.ShouldBeOfType<GoAroundNoted>();
        decision.NextContext.Phase.ShouldBe(FlightPhase.Airborne);
        decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Theory]
    [InlineData("autorizado circuito de tráfego pista uno uno, Papa Romeo Alfa Bravo Charlie")]
    [InlineData("circuito pista uno uno reporto perna do vento, Papa Romeo Alfa Bravo Charlie")]
    public void A_correct_circuit_readback_with_the_callsign_gets_no_answer(string said)
    {
        var cleared = Handle(new RequestLanding(Own), Flying).NextContext;

        var decision = Handle(Said(said), cleared);

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Theory]
    [InlineData("circuito de tráfego pista uno cinco, Papa Romeo Alfa Bravo Charlie", "wrong runway")]
    [InlineData("pista uno uno, Papa Romeo Alfa Bravo Charlie", "no word of the circuit")]
    [InlineData("pouso pista uno uno, Papa Romeo Alfa Bravo Charlie", "the landing is not what was cleared")]
    public void A_wrong_circuit_readback_is_corrected_and_stays_pending(string said, string why)
    {
        var cleared = Handle(new RequestLanding(Own), Flying).NextContext;

        var decision = Handle(Said(said), cleared);

        decision.Instruction.ShouldBe(new RequestReadbackCorrection(new CircuitClearance(Runway)), why);
        decision.NextContext.PendingReadback.ShouldBe(new CircuitClearance(Runway));
    }

    [Theory]
    [InlineData("pista uno uno pouso autorizado, Papa Romeo Alfa Bravo Charlie")]
    [InlineData("autorizado pousar uno uno, Papa Romeo Alfa Bravo Charlie")]
    public void A_correct_landing_readback_gets_no_answer(string said)
    {
        var cleared = Handle(new ReportFinal(Own), Flying with { Phase = FlightPhase.OnBase, AssignedRunway = "11" }).NextContext;

        Handle(Said(said), cleared).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Theory]
    [InlineData("pista uno uno, Papa Romeo Alfa Bravo Charlie")]
    [InlineData("pouso pista uno cinco, Papa Romeo Alfa Bravo Charlie")]
    public void A_wrong_landing_readback_is_corrected(string said)
    {
        var cleared = Handle(new ReportFinal(Own), Flying with { Phase = FlightPhase.OnBase, AssignedRunway = "11" }).NextContext;

        Handle(Said(said), cleared).Instruction.ShouldBe(new RequestReadbackCorrection(new LandingClearance("11")));
    }

    // Art. 12 does not list the reports among what must be read back: they are taken in silence, with or without the callsign.
    [Theory]
    [InlineData("reporto perna base")]
    [InlineData("ciente")]
    public void The_readback_of_a_request_to_report_is_taken_in_silence(string said)
    {
        var told = Handle(new ReportDownwind(Own), Flying with { Phase = FlightPhase.InCircuit }).NextContext;

        Handle(new Readback(null, said), told).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void Without_the_callsign_the_report_is_held_until_the_pilot_says_it()
    {
        var decision = Handle(new ReportBaseLeg(null), Flying);

        decision.Instruction.ShouldBeOfType<RequestCallsign>();
        decision.NextContext.AwaitingCallsign.ShouldBe(new ReportBaseLeg(null));
    }

    [Fact]
    public void An_airport_without_a_usable_runway_cannot_clear_a_landing()
    {
        var closed = AirportContext.Create(TestAirports.Airport("SBXX", [], AirportFrequencyType.Tower));

        Handle(new RequestLanding(Own), Flying, closed).Instruction.ShouldBeOfType<NoRunwayAvailable>();
    }

    [Fact]
    public void The_solo_does_not_handle_arrivals_and_says_where_to_call()
    {
        var decision = Handle(new RequestLanding(Own), Flying, OnSolo);

        decision.Instruction.ShouldBe(new CallTheTower(OnSolo.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency));
        decision.NextContext.Phase.ShouldBe(FlightPhase.Airborne);
    }

    [Fact]
    public void An_airport_with_only_a_torre_does_the_whole_arrival()
    {
        var onlyTower = AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)], AirportFrequencyType.Tower));

        var circuit = Handle(new RequestLanding(Own), Flying, onlyTower);
        var landing = Handle(new ReportFinal(Own), circuit.NextContext, onlyTower);

        circuit.Instruction.ShouldBeOfType<CircuitClearance>();
        landing.Instruction.ShouldBeOfType<LandingClearance>();
    }

    [Fact]
    public void Asking_to_say_it_again_repeats_the_last_instruction()
    {
        var circuit = Handle(new RequestLanding(Own), Flying).NextContext;

        Handle(new SayAgain(Own), circuit).Instruction.ShouldBe(new CircuitClearance(Runway));
    }
}
