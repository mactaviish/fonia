using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Nlu;

// What the controller does when the recognizer writes the pilot badly: words lost or split, the callsign misspelled. The sentences are
// invented; each one is the kind of slip the recognizer makes.
public class RecognizerSlipsTests
{
    private const string Own = "PRFAB";

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
        new(Own, phase, "SBXX", pending, pending, null, "15");

    // A readback with the words of the clearance and no "autorizado" is a readback, not a call to the controller.
    [Theory]
    [InlineData("Alinhe e mantenha, Foxtroot Alfa Bravo.")]
    [InlineData("Alinhe e mantenha o Foxtrot Alfa Bravo.")]
    public void A_line_up_readback_without_the_runway_is_taken_and_not_answered_as_a_call(string said)
    {
        var decision = Say(said, Flight(FlightPhase.LineUpCleared, new LineUpAndWait("15")), OnTower);

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Fact]
    public void A_report_of_having_lined_up_is_still_a_report_and_not_a_readback()
    {
        _nlu.Interpret("Torre, Foxtrot Alfa Bravo, alinhei", Flight(FlightPhase.Taxiing, null)).ShouldBeOfType<ReportLinedUp>();
    }

    [Theory]
    [InlineData("Torre, Papa Romeo Foxtrot Alfa Bravo, solicita, pousa.")]
    [InlineData("Foxtroot Alfa Bravo, solicita, pouso e... completo.")]
    public void Asking_to_land_in_any_form_of_the_word_is_understood(string said)
    {
        Say(said, Flight(FlightPhase.Airborne, new AirborneNotice(28)), OnTower).Instruction.ShouldBeOfType<CircuitClearance>();
    }

    // In the circuit the recognizer loses "perna": the word "vento" alone is the downwind report.
    [Theory]
    [InlineData("Na, vento, Foxtroot Alfa Bravo.")]
    [InlineData("Torre, do vento, Foxtrot Alfa Bravo.")]
    public void In_the_circuit_the_word_vento_alone_is_the_downwind_report(string said)
    {
        Say(said, Flight(FlightPhase.InCircuit, null), OnTower).Instruction.ShouldBeOfType<ReportBase>();
    }

    [Fact]
    public void Outside_the_circuit_the_word_vento_is_not_a_downwind_report()
    {
        Say("Torre, Foxtrot Alfa Bravo, vento", Flight(FlightPhase.Taxiing, null), OnTower).Instruction.ShouldNotBeOfType<ReportBase>();
    }

    [Theory]
    [InlineData("Foxtroot Alfa Bravo solicita a taxi para o patch.")]
    [InlineData("Solo, Papa Romeo Foxtrot Alfa Bravo, solicito táxi para o pátio")]
    public void After_landing_the_taxi_is_to_the_apron(string said)
    {
        var decision = Say(said, Flight(FlightPhase.Landed, new LandedNotice(33, null)), OnSolo);

        decision.Instruction.ShouldBeOfType<TaxiToApron>();
        decision.NextContext.Phase.ShouldBe(FlightPhase.Parked);
    }

    [Fact]
    public void Before_taking_off_the_taxi_is_still_to_the_holding_point()
    {
        Say("Foxtrot Alfa Bravo, pronto para o táxi", Flight(FlightPhase.Parked, null), OnSolo).Instruction.ShouldBeOfType<TaxiClearance>();
    }

    // The callsign misspelled in a readback that is right in everything else.
    [Fact]
    public void A_phonetic_word_with_two_slips_is_still_that_letter()
    {
        Say("Alinhe e mantenha o Foxroj Alfa Bravo, pista uno cinco.", Flight(FlightPhase.LineUpCleared, new LineUpAndWait("15")), OnTower)
            .Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void Fox_and_troc_in_two_words_are_foxtrot()
    {
        Say("Pouso autorizado, Pista Uno Cinco Fox Troc Alfa Bravo.", Flight(FlightPhase.LandingCleared, new LandingClearance("15")), OnTower)
            .Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void When_only_the_end_of_the_callsign_is_clear_the_readback_that_is_right_is_taken()
    {
        Say("Autorizado táxi para o ponto de espera da Pista Uno Cinco, foco de strote Alfa Bravo.", Flight(FlightPhase.Taxiing, new TaxiClearance("15")), OnSolo)
            .Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void The_end_of_the_callsign_does_not_make_a_wrong_readback_right_and_does_not_replace_the_callsign_in_a_request()
    {
        Say("Autorizado táxi para o ponto de espera da Pista Uno Nove, foco de strote Alfa Bravo.", Flight(FlightPhase.Taxiing, new TaxiClearance("15")), OnSolo)
            .Instruction.ShouldBeOfType<RequestReadbackCorrection>();
        Say("Solicito acionamento, Alfa Bravo.", Flight(FlightPhase.Parked, null), OnSolo).Instruction.ShouldBeOfType<RequestCallsign>();
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
        SpokenText.IsSameCallsign($"Papa Romeo {word} Alfa Bravo", Own).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Fostrito")]
    [InlineData("Banana")]
    public void A_word_that_only_looks_a_little_like_one_is_not_a_letter(string word)
    {
        SpokenText.IsSameCallsign($"Papa Romeo {word} Alfa Bravo", Own).ShouldBeFalse();
    }
}
