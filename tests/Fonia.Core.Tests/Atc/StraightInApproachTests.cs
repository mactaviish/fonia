using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// The straight-in approach (MCA 100-16/2025, Art. 129: "autorizada aproximação direta, pista 11, vento ..., QNH .... Reporte final").
public class StraightInApproachTests
{
    private const string Own = "PRABC";

    private static readonly AirportContext SoloAndTower = AirportContext.Create(TestAirports.WithSolo());

    private static readonly AirportContext OnTower =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private static readonly AirportContext OnSolo =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency);

    private static readonly FlightContext Flying = new(Own, FlightPhase.Airborne, "SBXX", null);

    private static readonly string Runway = OnTower.RunwayInUse!;

    private readonly AtcEngine _engine = new();

    private AtcDecision Ask(FlightContext flight, AirportContext? airport = null) =>
        _engine.Handle(new RequestLanding(Own, StraightIn: true), flight, airport ?? OnTower);

    private AtcDecision Say(string text, FlightContext flight) =>
        _engine.Handle(new Readback(Own, string.Join(' ', SpokenTokens.Tokenize(text))), flight, OnTower);

    [Fact]
    public void The_pilot_asking_for_a_straight_in_approach_is_cleared_and_told_to_report_final()
    {
        var decision = Ask(Flying);

        decision.Instruction.ShouldBe(new StraightInClearance(Runway));
        decision.NextContext.Phase.ShouldBe(FlightPhase.OnBase);
        decision.NextContext.PendingReadback.ShouldBe(new StraightInClearance(Runway));
        decision.NextContext.LastInstruction.ShouldBe(new StraightInClearance(Runway));
        decision.NextContext.AssignedRunway.ShouldBe(Runway);
    }

    [Fact]
    public void The_wind_and_the_altimeter_setting_go_into_the_clearance_when_there_are_any()
    {
        var weather = OnTower with { Wind = new Wind(110, 12), Qnh = 1013 };

        Ask(Flying, weather).Instruction.ShouldBe(new StraightInClearance(Runway, new Wind(110, 12), 1013));
    }

    [Fact]
    public void Reporting_final_after_it_is_cleared_to_land_on_the_same_runway()
    {
        var cleared = Ask(Flying).NextContext;

        var decision = _engine.Handle(new ReportFinal(Own), cleared, OnTower);

        decision.Instruction.ShouldBe(new LandingClearance(Runway));
        decision.NextContext.Phase.ShouldBe(FlightPhase.LandingCleared);
    }

    [Fact]
    public void Asking_again_from_the_circuit_starts_the_approach_over()
    {
        var inCircuit = Flying with { Phase = FlightPhase.OnDownwind, AssignedRunway = Runway };

        var decision = Ask(inCircuit);

        decision.Instruction.ShouldBe(new StraightInClearance(Runway));
        decision.NextContext.Phase.ShouldBe(FlightPhase.OnBase);
    }

    [Fact]
    public void An_ordinary_request_is_still_the_circuit()
    {
        _engine.Handle(new RequestLanding(Own), Flying, OnTower).Instruction.ShouldBe(new CircuitClearance(Runway));
    }

    [Fact]
    public void The_Solo_does_not_clear_it_but_sends_the_pilot_to_the_torre()
    {
        Ask(Flying, OnSolo).Instruction.ShouldBeOfType<CallTheTower>();
    }

    [Fact]
    public void An_airport_without_a_usable_runway_cannot_clear_it()
    {
        var noRunway = OnTower with { RunwayInUse = null };

        Ask(Flying, noRunway).Instruction.ShouldBe(new NoRunwayAvailable());
    }

    [Theory]
    [InlineData("autorizada aproximação direta pista uno um, reporto final, Alfa Bravo Charlie")]
    [InlineData("aproximação direta pista uno um, Papa Romeo Alfa Bravo Charlie")]
    [InlineData("aproximação direta, reporte final")]
    public void A_correct_readback_is_taken_in_silence(string said)
    {
        var cleared = Ask(Flying).NextContext;

        var decision = Say(said, cleared);

        decision.Instruction.ShouldBe(new NoResponse());
        decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Theory]
    [InlineData("autorizada aproximação direta pista uno cinco, Alfa Bravo Charlie")]
    [InlineData("autorizado circuito de tráfego pista uno um, Alfa Bravo Charlie")]
    public void A_readback_with_the_wrong_runway_or_the_wrong_approach_is_corrected(string said)
    {
        var cleared = Ask(Flying).NextContext;

        Say(said, cleared).Instruction.ShouldBe(new RequestReadbackCorrection(new StraightInClearance(Runway)));
    }

    // The join with the NLU: the readback of the clearance is not read as a new request.
    [Theory]
    [InlineData("Autorizada aproximação direta pista uno um, reporto final, Papa Romeo Alfa Bravo Charlie")]
    [InlineData("Aproximação direta pista uno um, Papa Romeo Alfa Bravo Charlie")]
    public void What_the_nlu_hears_after_the_clearance_is_a_readback_and_not_another_request(string said)
    {
        var cleared = Ask(Flying).NextContext;

        var decision = _engine.Handle(new RuleBasedNlu().Interpret(said, cleared), cleared, OnTower);

        decision.Instruction.ShouldBe(new NoResponse());
        decision.NextContext.Phase.ShouldBe(FlightPhase.OnBase);
    }

    [Fact]
    public void Asking_to_say_it_again_repeats_the_clearance()
    {
        var cleared = Ask(Flying).NextContext;

        _engine.Handle(new SayAgain(Own), cleared, OnTower).Instruction.ShouldBe(new StraightInClearance(Runway));
    }

    // Art. 129: "PTMAD, autorizada aproximação direta, pista 11, vento 150 graus, 15 kt, QNH 1013. Reporte final."
    [Fact]
    public void The_clearance_is_said_in_the_order_of_the_manual()
    {
        var phraseology = new PortuguesePhraseology();
        var context = new PhraseContext(Own, OnTower);

        phraseology.Speak(new StraightInClearance("11"), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, autorizada aproximação direta, PISTA UNO UNO, reporte final.");
        phraseology.Speak(new StraightInClearance("11", new Wind(150, 15), 1013), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, autorizada aproximação direta, PISTA UNO UNO, VENTO UNO CINCO ZERO GRAUS, UNO CINCO NÓS, ajuste de altímetro UNO ZERO UNO TRÊS, reporte final.");
    }

    [Fact]
    public void The_correction_repeats_the_clearance_after_negativo()
    {
        var phraseology = new PortuguesePhraseology();
        var context = new PhraseContext(Own, OnTower);

        phraseology.Speak(new RequestReadbackCorrection(new StraightInClearance("11")), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, negativo, autorizada aproximação direta, PISTA UNO UNO, reporte final.");
    }
}
