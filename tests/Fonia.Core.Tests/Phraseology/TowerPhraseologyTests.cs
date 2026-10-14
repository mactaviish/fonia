using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Phraseology;

// MCA 100-16/2025, Art. 126, II: callsign, runway and clearance, then the wind.
public class TowerPhraseologyTests
{
    private const string Callsign = "PAPA ROMEO ALFA BRAVO CHARLIE";

    private readonly PortuguesePhraseology _phraseology = new();

    private static readonly AirportContext Airport =
        AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)], AirportFrequencyType.Ground, AirportFrequencyType.Tower));

    private string? Say(AtcInstruction instruction) => _phraseology.Speak(instruction, new PhraseContext("PRABC", Airport))?.Text;

    [Fact]
    public void The_line_up_names_the_runway_first_and_then_says_to_line_up_and_wait()
    {
        Say(new LineUpAndWait("11")).ShouldBe($"{Callsign}, PISTA UNO UNO, alinhe e mantenha.");
        Say(new LineUpAndWait("23L")).ShouldBe($"{Callsign}, PISTA DOIS TRÊS ESQUERDA, alinhe e mantenha.");
    }

    [Fact]
    public void The_takeoff_clearance_is_runway_then_clearance()
    {
        Say(new TakeoffClearance("11")).ShouldBe($"{Callsign}, PISTA UNO UNO, autorizado decolagem.");
    }

    [Fact]
    public void The_wind_comes_last_when_there_is_one()
    {
        Say(new TakeoffClearance("09", new Wind(95, 10)))
            .ShouldBe($"{Callsign}, PISTA ZERO NOVE, autorizado decolagem, VENTO ZERO NOVE CINCO GRAUS, UNO ZERO NÓS.");
        Say(new TakeoffClearance("09", new Wind(0, 0))).ShouldBe($"{Callsign}, PISTA ZERO NOVE, autorizado decolagem, VENTO CALMO.");
    }

    // Art. 12, § 1: the wrong readback gets "negativo" and the right version.
    [Fact]
    public void A_wrong_readback_gets_negativo_and_the_right_version()
    {
        Say(new RequestReadbackCorrection(new LineUpAndWait("11"))).ShouldBe($"{Callsign}, negativo, PISTA UNO UNO, alinhe e mantenha.");
        Say(new RequestReadbackCorrection(new TakeoffClearance("11"))).ShouldBe($"{Callsign}, negativo, PISTA UNO UNO, autorizado decolagem.");
    }

    // Art. 50: "decolagem" is said only to clear a takeoff or to cancel one.
    [Fact]
    public void The_word_decolagem_is_said_only_in_the_takeoff_clearance()
    {
        Say(new LineUpAndWait("11"))!.ShouldNotContain("decolagem");
        Say(new HandoffToTower(new Frequency(118100)))!.ShouldNotContain("decolagem");
        Say(new TakeoffClearance("11"))!.ShouldContain("decolagem");
    }

    [Fact]
    public void The_torre_that_does_not_do_the_solos_job_is_a_notice_with_the_solos_frequency()
    {
        var onTower = new PhraseContext("PRABC", FrequencyResolver.Apply(Airport, Airport.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency));

        var utterance = _phraseology.Speak(new CallTheSolo(new Frequency(118000)), onTower)!;

        utterance.Kind.ShouldBe(UtteranceKind.Notice);
        utterance.Text.ShouldContain("TORRE Curitiba");
        utterance.Text.ShouldContain("acionamento");
        utterance.Text.ShouldContain("Solo em 118.0");
    }

    // Art. 129: the clearance, the runway, the wind and what to report.
    [Fact]
    public void The_circuit_clearance_names_the_runway_then_the_wind_and_asks_for_the_downwind()
    {
        Say(new CircuitClearance("11")).ShouldBe($"{Callsign}, autorizado para o circuito de tráfego, PISTA UNO UNO, reporte perna do vento.");
        Say(new CircuitClearance("11", new Wind(110, 12)))
            .ShouldBe($"{Callsign}, autorizado para o circuito de tráfego, PISTA UNO UNO, VENTO UNO UNO ZERO GRAUS, UNO DOIS NÓS, reporte perna do vento.");
    }

    // Art. 130, 131: the next report.
    [Fact]
    public void The_reports_of_the_circuit_are_asked_one_at_a_time()
    {
        Say(new ReportBase()).ShouldBe($"{Callsign}, reporte perna base.");
        Say(new ReportOnFinal()).ShouldBe($"{Callsign}, reporte na final.");
    }

    // Art. 132, II: the runway, the clearance, then the wind. The word "pouso", not "decolagem" (Art. 50).
    [Fact]
    public void The_landing_clearance_is_runway_then_clearance_then_wind()
    {
        Say(new LandingClearance("11")).ShouldBe($"{Callsign}, PISTA UNO UNO, pouso autorizado.");
        Say(new LandingClearance("09", new Wind(95, 10)))
            .ShouldBe($"{Callsign}, PISTA ZERO NOVE, pouso autorizado, VENTO ZERO NOVE CINCO GRAUS, UNO ZERO NÓS.");
    }

    [Fact]
    public void A_wrong_readback_of_the_circuit_or_the_landing_gets_negativo_and_the_right_version()
    {
        Say(new RequestReadbackCorrection(new CircuitClearance("11")))
            .ShouldBe($"{Callsign}, negativo, autorizado para o circuito de tráfego, PISTA UNO UNO, reporte perna do vento.");
        Say(new RequestReadbackCorrection(new LandingClearance("11"))).ShouldBe($"{Callsign}, negativo, PISTA UNO UNO, pouso autorizado.");
    }

    [Fact]
    public void The_Solo_asked_for_an_arrival_is_a_notice_with_the_torres_frequency()
    {
        var onSolo = new PhraseContext("PRABC", FrequencyResolver.Apply(Airport, Airport.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency));

        var utterance = _phraseology.Speak(new CallTheTower(new Frequency(119500)), onSolo)!;

        utterance.Kind.ShouldBe(UtteranceKind.Notice);
        utterance.Text.ShouldContain("SOLO Curitiba");
        utterance.Text.ShouldContain("Torre em 119.5");
    }

    // TODO(fraseologia): the manual has no phrase for taking in a go-around besides the handoff of Art. 136.
    [Fact]
    public void The_go_around_is_acknowledged_with_ciente()
    {
        Say(new GoAroundNoted()).ShouldBe($"{Callsign}, ciente.");
    }
}
