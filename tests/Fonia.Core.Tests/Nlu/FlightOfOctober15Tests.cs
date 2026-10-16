using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Nlu;

// What went wrong in the flight of 2026-10-15 (SBCT, PR-FRA), with the words the recognizer wrote: the lines come from the log of the pilot.
public class FlightOfOctober15Tests
{
    private const string Own = "PRFRA";

    private static readonly AirportContext SoloAndTower = AirportContext.Create(TestAirports.WithSolo());

    private static readonly AirportContext OnTower =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private static readonly AirportContext OnSolo =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency);

    private readonly RuleBasedNlu _nlu = new();
    private readonly AtcEngine _engine = new();

    private AtcDecision Say(string said, FlightContext flight, AirportContext airport) =>
        _engine.Handle(_nlu.Interpret(said, flight), flight, airport);

    private static FlightContext Flight(FlightPhase phase, AtcInstruction? pending) =>
        new(Own, phase, "SBCT", pending, pending, null, "15");

    // 1. "Alinhe e mantenha, Foxtroot Romeo Alfa." was answered "prossiga": the readback has no "autorizado", and no runway either.
    [Theory]
    [InlineData("Alinhe e mantenha, Foxtroot Romeo Alfa.")]
    [InlineData("Alinhe e mantenha o Foxtrot Romeo Alfa.")]
    public void A_line_up_readback_without_the_runway_is_taken_and_not_answered_as_a_call(string said)
    {
        var decision = Say(said, Flight(FlightPhase.LineUpCleared, new LineUpAndWait("15")), OnTower);

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Fact]
    public void A_report_of_having_lined_up_is_still_a_report_and_not_a_readback()
    {
        _nlu.Interpret("Torre Curitiba, Foxtrot Romeo Alfa, alinhei", Flight(FlightPhase.Taxiing, null)).ShouldBeOfType<ReportLinedUp>();
    }

    // 2. "Tore Curitiba, Papa Romeo Foxtrot Romeo Alfa, solicita, pousa." was answered "repita".
    [Theory]
    [InlineData("Tore Curitiba, Papa Romeo Foxtrot Romeo Alfa, solicita, pousa.")]
    [InlineData("Foxtroot Romeo Alfa, solicita, pouso e... completo.")]
    public void Asking_to_land_in_any_form_of_the_word_is_understood(string said)
    {
        Say(said, Flight(FlightPhase.Airborne, new AirborneNotice(28)), OnTower).Instruction.ShouldBeOfType<CircuitClearance>();
    }

    // 3. "Nado, vento, Foxtroot Romeo Alfa." and "ToRRE Curitiba, do vento, Foxtrot Romeo Alfa." were answered "prossiga":
    //    the recognizer lost "perna", and the pilot is in the circuit.
    [Theory]
    [InlineData("Nado, vento, Foxtroot Romeo Alfa.")]
    [InlineData("ToRRE Curitiba, do vento, Foxtrot Romeo Alfa.")]
    public void In_the_circuit_the_word_vento_alone_is_the_downwind_report(string said)
    {
        Say(said, Flight(FlightPhase.InCircuit, null), OnTower).Instruction.ShouldBeOfType<ReportBase>();
    }

    [Fact]
    public void Outside_the_circuit_the_word_vento_is_not_a_downwind_report()
    {
        Say("Torre Curitiba, Foxtrot Romeo Alfa, vento", Flight(FlightPhase.Taxiing, null), OnTower).Instruction.ShouldNotBeOfType<ReportBase>();
    }

    // 4. After landing, "solicita táxi para o pátio" got a taxi clearance to the holding point of the runway.
    [Theory]
    [InlineData("Foxtroot Romeo Alfa solicita a taxi para o patch.")]
    [InlineData("Solo Curitiba, Papa Romeo Foxtrot Romeo Alfa, solicito táxi para o pátio")]
    public void After_landing_the_taxi_is_to_the_apron(string said)
    {
        var decision = Say(said, Flight(FlightPhase.Landed, new LandedNotice(33, null)), OnSolo);

        decision.Instruction.ShouldBeOfType<TaxiToApron>();
        decision.NextContext.Phase.ShouldBe(FlightPhase.Parked);
    }

    [Fact]
    public void Before_taking_off_the_taxi_is_still_to_the_holding_point()
    {
        Say("Foxtrot Romeo Alfa, pronto para o táxi", Flight(FlightPhase.Parked, null), OnSolo).Instruction.ShouldBeOfType<TaxiClearance>();
    }

    // 5. The callsign written badly in a readback that is right: "negativo" three times.
    [Fact]
    public void Foxroj_is_foxtrot()
    {
        Say("Alinhe e mantenha o Foxroj Romeo Alfa, pista uno cinco.", Flight(FlightPhase.LineUpCleared, new LineUpAndWait("15")), OnTower)
            .Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void Fox_and_troc_in_two_words_are_foxtrot()
    {
        Say("Pouso autorizado, Pista Uno Cinco Fox Troc Romeo Alfa.", Flight(FlightPhase.LandingCleared, new LandingClearance("15")), OnTower)
            .Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void When_only_the_end_of_the_callsign_is_clear_the_readback_that_is_right_is_taken()
    {
        Say("Autorizado táxi para o ponto de espera da Pista Uno Cinco, foco de strote Romeo Alfa.", Flight(FlightPhase.Taxiing, new TaxiClearance("15")), OnSolo)
            .Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void The_end_of_the_callsign_does_not_make_a_wrong_readback_right_and_does_not_replace_the_callsign_in_a_request()
    {
        Say("Autorizado táxi para o ponto de espera da Pista Uno Nove, foco de strote Romeo Alfa.", Flight(FlightPhase.Taxiing, new TaxiClearance("15")), OnSolo)
            .Instruction.ShouldBeOfType<RequestReadbackCorrection>();
        Say("Solicito acionamento, Romeo Alfa.", Flight(FlightPhase.Parked, null), OnSolo).Instruction.ShouldBeOfType<RequestCallsign>();
    }

    [Fact]
    public void Another_pair_of_letters_at_the_end_is_not_this_aircraft()
    {
        Say("Autorizado táxi para o ponto de espera da Pista Uno Cinco, Bravo Charlie.", Flight(FlightPhase.Taxiing, new TaxiClearance("15")), OnSolo)
            .Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }

    [Theory]
    [InlineData("Foxroj")]
    [InlineData("Foxtroch")]
    [InlineData("Foxtroat")]
    [InlineData("Foxtroot")]
    public void The_long_phonetic_words_stand_two_letters_of_slip_when_they_start_alike(string word)
    {
        SpokenText.IsSameCallsign($"Papa Romeo {word} Romeo Alfa", Own).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Fostrito")]
    [InlineData("Banana")]
    public void A_word_that_only_looks_a_little_like_one_is_not_a_letter(string word)
    {
        SpokenText.IsSameCallsign($"Papa Romeo {word} Romeo Alfa", Own).ShouldBeFalse();
    }
}
