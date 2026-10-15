using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;
using Fonia.Core.Weather;

namespace Fonia.Core.Tests.Atc;

// "Solicita informações de partida" (MCA 100-16/2025, Art. 121) and the answer of Art. 122: the runway in use, the wind, the altimeter
// setting, the temperature, the visibility and the time check, in that order.
public class DepartureInformationTests
{
    private const string Own = "PRABC";

    private static readonly DateTimeOffset Noon = new(2026, 10, 15, 20, 2, 30, TimeSpan.Zero);

    private static readonly AirportContext Plain = AirportContext.Create(TestAirports.WithSolo());

    private static readonly AirportContext WithWeather = Plain.WithWeather(
        new Metar("SBXX", new Wind(230, 8), 24, 1013, false, "raw", 9999), keepRunway: false);

    private static readonly FlightContext Parked = new(Own, FlightPhase.Parked, "SBXX", null);

    private readonly RuleBasedNlu _nlu = new();
    private readonly PortuguesePhraseology _phraseology = new();

    private string? Speak(AtcInstruction instruction, AirportContext airport, DateTimeOffset? now = null) =>
        _phraseology.Speak(instruction, new PhraseContext(Own, airport, now))?.Text;

    [Theory]
    [InlineData("Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicita condições do aeródromo")]
    [InlineData("Solo Curitiba, PRABC, solicito informações de partida")]
    [InlineData("PRABC, informações de partida")]
    [InlineData("Papa Romeo Alfa Bravo Charlie, solicito informações meteorológicas")]
    [InlineData("Solo, PRABC, qual o tempo no aeródromo?")]
    [InlineData("PRABC solicita o QNH")]
    [InlineData("Solo, Alfa Bravo Charlie, solicito o ajuste de altímetro")]
    [InlineData("Bom dia, PRABC, solicito METAR")]
    public void The_pilot_asking_for_the_conditions_is_understood_in_any_wording(string said)
    {
        _nlu.Interpret(said, Parked).ShouldBe(new RequestInformation(Own));
        _nlu.Interpret(said, new FlightContext(Own, FlightPhase.Taxiing, "SBXX", null)).ShouldBe(new RequestInformation(Own));
    }

    [Theory]
    [InlineData("Solo Curitiba, PRABC, solicito acionamento")]
    [InlineData("PRABC solicito informações e acionamento")]
    [InlineData("PRABC, pronto para o táxi")]
    [InlineData("PRABC, solicito informações de táxi")]
    [InlineData("PRABC, pushback e acionamento")]
    public void A_request_for_something_else_is_not_a_request_for_information(string said)
    {
        _nlu.Interpret(said, Parked).ShouldNotBeOfType<RequestInformation>();
    }

    [Theory]
    [InlineData("autorizado táxi para o ponto de espera da pista uno cinco, ajuste de altímetro uno zero um três, PRABC")]
    [InlineData("pista uno cinco, altímetro uno zero um três, PRABC")]
    public void The_readback_of_a_clearance_with_the_altimeter_setting_is_not_a_request(string said)
    {
        var flight = new FlightContext(Own, FlightPhase.Taxiing, "SBXX", new TaxiClearance("15", 1013));

        _nlu.Interpret(said, flight).ShouldBeOfType<Readback>();
    }

    [Theory]
    [InlineData("reporte perna do vento, ajuste de altímetro uno zero um três, PRABC")]
    [InlineData("circuito de tráfego, pista uno cinco, perna do vento, PRABC")]
    public void The_circuit_words_are_not_a_request_for_information(string said)
    {
        _nlu.Interpret(said, new FlightContext(Own, FlightPhase.InCircuit, "SBXX", new CircuitClearance("15", null, 1013))).ShouldNotBeOfType<RequestInformation>();
    }

    [Fact]
    public void The_pilot_asking_for_information_before_the_circuit_still_gets_the_landing_instructions()
    {
        _nlu.Interpret("Torre, PRABC, solicito informações para pouso", new FlightContext(Own, FlightPhase.Airborne, "SBXX", null))
            .ShouldBe(new RequestLanding(Own));
    }

    [Fact]
    public void The_engine_answers_with_what_the_airport_knows_and_does_not_move_the_flight()
    {
        var decision = new AtcEngine().Handle(new RequestInformation(Own), Parked, WithWeather);

        decision.Instruction.ShouldBe(new DepartureInformation(WithWeather.RunwayInUse, WithWeather.Wind, 1013, 24, 9999));
        decision.NextContext.Phase.ShouldBe(FlightPhase.Parked);
        decision.NextContext.PendingReadback.ShouldBeNull();
        decision.NextContext.LastInstruction.ShouldBe(decision.Instruction);
    }

    [Fact]
    public void Without_a_metar_the_airport_knows_only_the_runway()
    {
        new AtcEngine().Handle(new RequestInformation(Own), Parked, Plain).Instruction.ShouldBe(new DepartureInformation(Plain.RunwayInUse, null, null, null, null));
    }

    [Fact]
    public void Without_the_callsign_the_request_is_held_until_the_pilot_says_it()
    {
        new AtcEngine().Handle(new RequestInformation(null), Parked, WithWeather).Instruction.ShouldBeOfType<RequestCallsign>();
    }

    [Fact]
    public void An_afis_gives_the_conditions_of_the_aerodrome_and_nobody_gives_nothing()
    {
        var afis = AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)], AirportFrequencyType.Afis));
        var nobody = AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)]));

        new AtcEngine().Handle(new RequestInformation(Own), Parked, afis).Instruction.ShouldBeOfType<DepartureInformation>();
        new AtcEngine().Handle(new RequestInformation(Own), Parked, nobody).Instruction.ShouldBeOfType<NoControlService>();
    }

    [Fact]
    public void The_torre_gives_the_information_too()
    {
        var onTower = FrequencyResolver.Apply(WithWeather, WithWeather.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

        new AtcEngine().Handle(new RequestInformation(Own), Parked, onTower).Instruction.ShouldBeOfType<DepartureInformation>();
    }

    // Art. 122: "pista 09, vento 100/8 kt, ajuste de altímetro 1012, temperatura 28, RVR 1.000m, hora certa 17."
    [Fact]
    public void The_answer_has_the_items_of_the_manual_in_its_order()
    {
        var information = new DepartureInformation("15", new Wind(240, 3), 1013, 24, 9999);

        Speak(information, WithWeather, Noon).ShouldBe(
            "PAPA ROMEO ALFA BRAVO CHARLIE, PISTA UNO CINCO, VENTO DOIS QUATRO ZERO GRAUS, TRÊS NÓS, ajuste de altímetro UNO ZERO UNO TRÊS, temperatura DOIS QUATRO, visibilidade DEZ QUILÔMETROS OU MAIS, hora certa ZERO DOIS.");
    }

    [Fact]
    public void What_is_not_known_is_not_said_and_the_time_check_needs_a_clock()
    {
        Speak(new DepartureInformation("15", null, null, null, null), Plain).ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, PISTA UNO CINCO.");
        Speak(new DepartureInformation("15", null, null, null, null), Plain, Noon).ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, PISTA UNO CINCO, hora certa ZERO DOIS.");
    }

    [Theory]
    [InlineData(3000, "visibilidade TRÊS MIL METROS")]
    [InlineData(1000, "visibilidade UNO MIL METROS")]
    [InlineData(700, "visibilidade SETE ZERO ZERO METROS")]
    [InlineData(1700, "visibilidade UNO SETE ZERO ZERO METROS")]
    [InlineData(9999, "visibilidade DEZ QUILÔMETROS OU MAIS")]
    public void The_visibility_is_said_as_the_manual_says_distances_in_meters(int meters, string expected)
    {
        Speak(new DepartureInformation("15", null, null, null, meters), Plain)!.ShouldContain(expected, Case.Sensitive);
    }

    [Theory]
    [InlineData(24, "temperatura DOIS QUATRO")]
    [InlineData(5, "temperatura ZERO CINCO")]
    [InlineData(-2, "temperatura MENOS ZERO DOIS")]
    public void The_temperature_is_two_digits_and_minus_when_it_is_below_zero(int degrees, string expected)
    {
        Speak(new DepartureInformation("15", null, null, degrees, null), Plain)!.ShouldContain(expected, Case.Sensitive);
    }

    [Fact]
    public void Asking_to_say_it_again_repeats_the_information()
    {
        var engine = new AtcEngine();
        var told = engine.Handle(new RequestInformation(Own), Parked, WithWeather).NextContext;

        engine.Handle(new SayAgain(Own), told, WithWeather).Instruction.ShouldBeOfType<DepartureInformation>();
    }
}
