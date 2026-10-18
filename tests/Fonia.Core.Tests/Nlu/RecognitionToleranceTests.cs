using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Nlu;

// What the recognizer writes when it hears the pilot badly: a split word, a letter too many. A pilot must not have to repeat what the
// controller asked for because of one word the recognizer split or one letter it added: the readback is judged on what matters.
public class RecognitionToleranceTests
{
    private const string Own = "PRFAB";

    private static readonly AirportContext SoloAndTower = AirportContext.Create(TestAirports.WithSolo());

    private static readonly AirportContext OnTower =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private static readonly AirportContext OnSolo =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency);

    private static readonly string Runway = SoloAndTower.RunwayInUse!;

    private readonly RuleBasedNlu _nlu = new();
    private readonly AtcEngine _engine = new();

    private AtcDecision Say(string said, FlightContext flight, AirportContext airport) =>
        _engine.Handle(_nlu.Interpret(said, flight), flight, airport);

    private static FlightContext Flight(FlightPhase phase, AtcInstruction? pending) =>
        new(Own, phase, "SBXX", pending, pending, null, Runway);

    [Theory]
    [InlineData("Foxtroot Alfa Bravo, autorizado de colagem, pista uno uno.")]
    [InlineData("Foxtrot Alfa Bravo. Pista uno um. Autorizado de colagem.")]
    [InlineData("Autorizado decolagem pista uno uno, Papa Romeo Foxtrot Alfa Bravo.")]
    public void The_takeoff_readback_is_taken_when_the_recognizer_splits_decolagem(string said)
    {
        var decision = Say(said, Flight(FlightPhase.TakeoffCleared, new TakeoffClearance(Runway)), OnTower);

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Fact]
    public void A_takeoff_readback_without_the_runway_is_taken_because_the_pilot_is_on_it()
    {
        Say("Autorizado de colagem, o Papa Romeo Foxtroat Alfa Bravo.", Flight(FlightPhase.TakeoffCleared, new TakeoffClearance(Runway)), OnTower)
            .Instruction.ShouldBeOfType<NoResponse>();
    }

    [Theory]
    [InlineData("Autorizado, táxi para o ponto de espera da pista uno uno, o Fox Alfa Bravo.")]
    [InlineData("Autorizado táxi para o ponto de espera da pista uno uno, Foxtroot Alfa Bravo.")]
    public void Fox_is_foxtrot_in_the_callsign_of_a_readback(string said)
    {
        Say(said, Flight(FlightPhase.Taxiing, new TaxiClearance(Runway)), OnSolo).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void A_letter_too_many_in_the_callsign_does_not_make_it_another_aircraft()
    {
        var parked = Flight(FlightPhase.Parked, null);

        var intent = _nlu.Interpret("SOLO Curitiba Papa Romeo Foxtrot Alfa. Alfa Bravo, solicitacionamento.", parked);

        intent.ShouldBe(new RequestStartup(Own, null));
        Say("SOLO Curitiba Papa Romeo Foxtrot Alfa. Alfa Bravo, solicitacionamento.", parked, OnSolo).Instruction.ShouldBeOfType<StartupApproved>();
    }

    [Theory]
    [InlineData("Papa Romeo Foxtrot Alfa")]
    [InlineData("Papa Romeo Foxtrot Alfa Alfa Bravo")]
    [InlineData("Papa Romeo Foxtroot Alfa Bravo")]
    public void One_letter_wrong_missing_or_extra_is_this_aircraft(string heard)
    {
        SpokenText.IsCloseCallsign(heard, Own).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Papa Romeo Alfa Bravo Charlie")]
    [InlineData("Papa Romeo Alfa Alfa Charlie")]
    [InlineData("Papa Romeo")]
    [InlineData("Papa Tango Foxtrot Tango Alfa")]
    public void Another_aircraft_is_not_taken_for_this_one(string heard)
    {
        SpokenText.IsCloseCallsign(heard, Own).ShouldBeFalse();
        _nlu.Interpret($"Solo Curitiba, {heard}, solicito acionamento", Flight(FlightPhase.Parked, null)).Callsign.ShouldBeNull();
    }

    [Fact]
    public void A_short_registration_or_a_company_callsign_is_never_matched_loosely()
    {
        SpokenText.IsCloseCallsign("Papa Romeo Alfa", "PRA").ShouldBeFalse();
        SpokenText.IsCloseCallsign("Gol um dois três", "GLO123").ShouldBeFalse();
    }

    // The readback of "reporte na final", written "Importará na final": it is not the pilot reporting final, which would clear the landing.
    [Theory]
    [InlineData("Importará na final o Papa Romeo Foxtroot Alfa Bravo.")]
    [InlineData("Reportará na final o Papa Romeo Foxtrot Alfa Bravo.")]
    public void The_readback_of_the_request_to_report_final_is_not_a_report_of_final(string said)
    {
        var onBase = Flight(FlightPhase.OnBase, new ReportOnFinal());

        var decision = Say(said, onBase, OnTower);

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.Phase.ShouldBe(FlightPhase.OnBase);
    }

    [Theory]
    [InlineData("Sente.")]
    [InlineData("Foxtroot Alfa Bravo Siente.")]
    [InlineData("Foxtroot Alfa Bravo, ciente da decolagem.")]
    [InlineData("Papa Romeo Foxtrot Alfa Bravo.")]
    public void The_acknowledgement_of_a_notice_is_taken_in_silence_however_it_was_written(string said)
    {
        var notice = new AirborneNotice(0);
        var decision = Say(said, Flight(FlightPhase.Airborne, notice), OnTower);

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Fact]
    public void Only_the_callsign_after_an_instruction_that_needs_a_readback_still_invites_to_go_ahead()
    {
        Say("Papa Romeo Foxtrot Alfa Bravo.", Flight(FlightPhase.Taxiing, new TaxiClearance(Runway)), OnSolo).Instruction.ShouldBeOfType<GoAhead>();
    }

    [Fact]
    public void Ciente_alone_with_nothing_to_acknowledge_is_still_not_understood()
    {
        Say("Sente.", Flight(FlightPhase.Parked, null), OnSolo).Instruction.ShouldBeOfType<SayAgainRequest>();
    }

    [Theory]
    [InlineData("Solo Curitiba. Papa Romeo Foxtroot Alfa Bravo. Solicitocionamento.")]
    [InlineData("SOLO Curitiba Papa Romeo Foxtrot Alfa Bravo solicitacionamento")]
    public void Solicito_and_acionamento_glued_together_are_still_a_request_for_startup(string said)
    {
        _nlu.Interpret(said, Flight(FlightPhase.Parked, null)).ShouldBe(new RequestStartup(Own, null));
    }

    [Fact]
    public void Chamando_torre_is_the_readback_of_the_handoff_and_takes_no_answer()
    {
        var handed = Flight(FlightPhase.HandedToTower, new HandoffToTower(new Frequency(118550)));

        Say("Chamando Torre Curitiba, Papa Romeo Foxtrot Alfa Bravo.", handed, OnSolo).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Theory]
    [InlineData("Torre Curitiba. Papa Romeo Foxtroot Alfa Bravo, aliñado.")]
    [InlineData("Torre Curitiba, Papa Romeo Foxtrot Alfa Bravo, alinado")]
    public void Alinhado_without_its_h_is_still_lined_up(string said)
    {
        Say(said, Flight(FlightPhase.LineUpCleared, new LineUpAndWait(Runway)), OnTower).Instruction.ShouldBeOfType<TakeoffClearance>();
    }

    [Fact]
    public void The_readback_of_the_taxi_to_the_apron_is_not_a_new_taxi_request()
    {
        var decision = Say("Táxi para o pátio, Papa Romeo Foxtroot Alfa Bravo.", Flight(FlightPhase.Parked, new TaxiToApron()), OnSolo);

        decision.Instruction.ShouldBeOfType<NoResponse>();
    }

    [Theory]
    [InlineData("TORRE Curitiba. Papa Romeo Foxtrot Alfa Bravo. Pernabase.")]
    [InlineData("Torre Curitiba, Papa Romeo Foxtrat Alfa Bravo, perna base")]
    public void The_report_of_the_base_leg_is_understood_when_the_words_are_glued_or_a_letter_is_off(string said)
    {
        _nlu.Interpret(said, Flight(FlightPhase.OnDownwind, new ReportBase())).ShouldBe(new ReportBaseLeg(Own));
    }

    [Fact]
    public void Reparto_na_final_is_the_readback_of_the_request_to_report_final_and_clears_nothing()
    {
        var decision = Say("Reparto na final Papa Romeo Foxroot Alfa Bravo.", Flight(FlightPhase.OnBase, new ReportOnFinal()), OnTower);

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.Phase.ShouldBe(FlightPhase.OnBase);
    }

    [Theory]
    [InlineData("Papa Romeo Foxtrat Alfa Bravo")]
    [InlineData("Papa Romeo Faxtrot Alfa Bravo")]
    public void A_phonetic_word_with_a_letter_changed_is_still_that_letter(string heard)
    {
        SpokenText.IsSameCallsign(heard, Own).ShouldBeTrue();
    }
}
