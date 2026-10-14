using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Phraseology;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Phraseology;

public class PortuguesePhraseologyTests
{
    private const string Callsign = "PAPA ROMEO ALFA BRAVO CHARLIE";

    private readonly PortuguesePhraseology _phraseology = new();

    private static PhraseContext At(params AirportFrequencyType[] frequencies) =>
        new("PRABC", AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)], frequencies)));

    private static readonly PhraseContext Solo = At(AirportFrequencyType.Ground, AirportFrequencyType.Tower);

    private string? Say(AtcInstruction instruction, PhraseContext? context = null) =>
        _phraseology.Speak(instruction, context ?? Solo)?.Text;

    // Art. 123, II (S-ACN-OK, S-PSH-OK) and the practice of VATSIM Brasil (P-ACN-PSH-OK).
    [Fact]
    public void Startup_and_pushback_follow_the_manual_and_the_practice()
    {
        Say(new StartupApproved()).ShouldBe($"{Callsign}, acionamento aprovado, chame pronto para o táxi.");
        Say(new PushbackApproved(false)).ShouldBe($"{Callsign}, pushback aprovado, reporte pronto para o táxi.");
        Say(new PushbackApproved(true)).ShouldBe($"{Callsign}, autorizado pushback e acionamento, chame pronto para o táxi.");
    }

    // Art. 125, II without the taxiway (S-TXI-OK-SHORT).
    [Theory]
    [InlineData("10", "UNO ZERO")]
    [InlineData("09", "ZERO NOVE")]
    [InlineData("23L", "DOIS TRÊS ESQUERDA")]
    public void The_taxi_clearance_goes_to_the_holding_point_of_the_runway(string runway, string spoken)
    {
        Say(new TaxiClearance(runway)).ShouldBe($"{Callsign}, autorizado táxi para o ponto de espera da PISTA {spoken}.");
    }

    // Art. 12: a wrong readback gets "negativo" and the right version.
    [Fact]
    public void A_wrong_readback_is_answered_with_negativo_and_the_right_version()
    {
        Say(new RequestReadbackCorrection(new TaxiClearance("11")))
            .ShouldBe($"{Callsign}, negativo, autorizado táxi para o ponto de espera da PISTA UNO UNO.");
        Say(new RequestReadbackCorrection(new StartupApproved()))
            .ShouldBe($"{Callsign}, negativo, acionamento aprovado, chame pronto para o táxi.");
    }

    [Fact]
    public void A_correct_readback_gets_no_words()
    {
        _phraseology.Speak(new NoResponse(), Solo).ShouldBeNull();
    }

    [Fact]
    public void What_was_not_understood_is_answered_with_the_callsign_and_repita()
    {
        // The manual has only "REPITA"; the callsign in front is how an aircraft is addressed, and the short word alone came out
        // badly in the voice model (live test, 2026-10-14).
        Say(new SayAgainRequest()).ShouldBe($"{Callsign}, repita.");
    }

    [Fact]
    public void The_pilot_who_only_called_is_told_to_go_ahead()
    {
        Say(new GoAhead()).ShouldBe($"{Callsign}, prossiga.");
    }

    [Theory]
    [InlineData(AirportFrequencyType.Ground, "SOLO")]
    [InlineData(AirportFrequencyType.Tower, "TORRE")]
    [InlineData(AirportFrequencyType.Afis, "RÁDIO")]
    public void Asking_for_the_callsign_names_the_unit_that_answers(AirportFrequencyType frequency, string unit)
    {
        Say(new RequestCallsign(), At(frequency)).ShouldBe($"Estação chamando {unit} Curitiba, repita o indicativo.");
    }

    [Fact]
    public void The_afis_says_it_only_informs()
    {
        var context = At(AirportFrequencyType.Afis);

        Say(new NoControlService(ServiceUnit.Afis), context).ShouldBe($"{Callsign}, RÁDIO Curitiba, sem serviço de controle neste aeródromo, apenas informações de voo.");
        _phraseology.Speak(new NoControlService(ServiceUnit.Afis), context)!.Kind.ShouldBe(UtteranceKind.Radio);
    }

    [Fact]
    public void Where_nobody_answers_the_app_tells_the_user_instead_of_speaking()
    {
        var utterance = _phraseology.Speak(new NoControlService(ServiceUnit.None), At())!;

        utterance.Kind.ShouldBe(UtteranceKind.Notice);
        utterance.Text.ShouldContain("SBXX");
    }

    [Fact]
    public void Without_a_runway_the_app_tells_the_user()
    {
        var context = new PhraseContext("PRABC", AirportContext.Create(TestAirports.Airport("SBXX", [], AirportFrequencyType.Ground)));

        var utterance = _phraseology.Speak(new NoRunwayAvailable(), context)!;

        utterance.Kind.ShouldBe(UtteranceKind.Notice);
        utterance.Text.ShouldContain("SBXX");
    }

    [Fact]
    public void A_company_callsign_is_spoken_with_its_telephony()
    {
        var context = Solo with { Callsign = "GLOGOW" };

        Say(new StartupApproved(), context).ShouldStartWith("GOL GOLF OSCAR WHISKEY, acionamento aprovado");
    }

    [Fact]
    public void Every_instruction_the_engine_can_give_has_words_at_every_kind_of_airport()
    {
        var engine = new AtcEngine();
        var nlu = new Fonia.Core.Nlu.RuleBasedNlu();
        string[] said =
        [
            "Solo, PRABC, solicito acionamento", "Solo, PRABC, pushback e acionamento", "Solo, PRABC, pushback", "PRABC, táxi",
            "solicito acionamento", "PRABC", "repita", "blá", "autorizado táxi pista um um, PRABC", "ciente, PRABC",
        ];
        var airports = new[]
        {
            Solo,
            At(AirportFrequencyType.Tower),
            At(AirportFrequencyType.Afis),
            At(),
            new PhraseContext("PRABC", AirportContext.Create(TestAirports.Airport("SBXX", [], AirportFrequencyType.Ground))),
        };

        foreach (var context in airports)
        {
            var flight = new FlightContext("PRABC", FlightPhase.Parked, "SBXX", null);

            foreach (var phrase in said)
            {
                var decision = engine.Handle(nlu.Interpret(phrase, flight), flight, context.Airport);
                flight = decision.NextContext;

                Should.NotThrow(() => _phraseology.Speak(decision.Instruction, context), phrase);
            }
        }
    }

    [Fact]
    public void An_instruction_with_no_phrase_is_refused_loudly()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Say(new RequestReadbackCorrection(new SayAgainRequest())));
    }
}
