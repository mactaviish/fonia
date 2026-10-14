using Fonia.Core.Atc;
using Fonia.Core.Nlu;

namespace Fonia.Core.Tests.Nlu;

// What the pilot says in the circuit (MCA 100-16/2025, Art. 128 to 132): the pilot may say it any way, the controller follows the manual.
public class ArrivalNluTests
{
    private const string Own = "PRABC";

    private static readonly FlightContext Flying = new(Own, FlightPhase.Airborne, "SBCT", null);

    private static readonly FlightContext Circuit = new(Own, FlightPhase.InCircuit, "SBCT", new CircuitClearance("15"));

    private static readonly FlightContext Cleared = new(Own, FlightPhase.LandingCleared, "SBCT", new LandingClearance("15"));

    private readonly RuleBasedNlu _nlu = new();

    // Art. 128: "Torre Brasília, TAM 3282, 5 milhas ao sul do aeródromo, 3.500 pés, instruções para pouso."
    [Theory]
    [InlineData("Torre Curitiba, PRABC, cinco milhas ao sul do aeródromo, três mil e quinhentos pés, instruções para pouso")]
    [InlineData("Torre Curitiba, Papa Romeo Alfa Bravo Charlie, solicito instruções para pouso")]
    [InlineData("PRABC solicita pouso")]
    [InlineData("Torre, PRABC, solicito entrada no circuito de tráfego")]
    [InlineData("PRABC, para pouso, dez milhas a oeste")]
    [InlineData("Torre, PRABC, solicito aproximação visual para a pista uno cinco")]
    public void The_pilot_asking_to_land_is_understood_in_any_wording(string said)
    {
        _nlu.Interpret(said, Flying).ShouldBe(new RequestLanding(Own));
        _nlu.Interpret(said, new FlightContext(Own, FlightPhase.Parked, "SBCT", null)).ShouldBe(new RequestLanding(Own));
    }

    // Art. 130, I: "Torre São José, PT BFA, perna do vento, pista 35."
    [Theory]
    [InlineData("Torre Curitiba, PRABC, perna do vento, pista uno cinco")]
    [InlineData("PRABC na perna do vento")]
    [InlineData("Torre, Papa Romeo Alfa Bravo Charlie, perna do vento")]
    public void The_pilot_on_the_downwind_is_understood(string said)
    {
        _nlu.Interpret(said, Circuit).ShouldBe(new ReportDownwind(Own));
    }

    // Art. 131, I: "Torre São José, PT BFA, perna base."
    [Theory]
    [InlineData("Torre Curitiba, PRABC, perna base")]
    [InlineData("PRABC na base pista uno cinco")]
    [InlineData("Papa Romeo Alfa Bravo Charlie, perna base")]
    public void The_pilot_on_the_base_leg_is_understood(string said)
    {
        _nlu.Interpret(said, Circuit).ShouldBe(new ReportBaseLeg(Own));
    }

    // Art. 132, I: "Torre São José, PT BFA, final." Also "na final, condições visuais", "final longa".
    [Theory]
    [InlineData("Torre Curitiba, PRABC, final")]
    [InlineData("PRABC na final, condições visuais")]
    [InlineData("PRABC final longa pista uno cinco")]
    [InlineData("PRABC, curta final")]
    public void The_pilot_on_final_is_understood(string said)
    {
        _nlu.Interpret(said, Circuit).ShouldBe(new ReportFinal(Own));
    }

    // Art. 136: "Torre Brasília, PT CTA, iniciando procedimento de aproximação perdida."
    [Theory]
    [InlineData("Torre Curitiba, PRABC, arremetendo")]
    [InlineData("PRABC, iniciando procedimento de aproximação perdida")]
    [InlineData("PRABC arremeto")]
    public void The_pilot_going_around_is_understood(string said)
    {
        _nlu.Interpret(said, Cleared).ShouldBe(new ReportGoingAround(Own));
    }

    [Theory]
    [InlineData("autorizado para o circuito de tráfego, pista uno cinco, reporte perna do vento, Papa Romeo Alfa Bravo Charlie")]
    [InlineData("circuito de tráfego pista uno cinco, Papa Romeo Alfa Bravo Charlie")]
    public void The_readback_of_the_circuit_clearance_is_not_a_new_request_to_land(string said)
    {
        _nlu.Interpret(said, new FlightContext(Own, FlightPhase.InCircuit, "SBCT", new CircuitClearance("15"))).ShouldBeOfType<Readback>();
    }

    [Theory]
    [InlineData("pista uno cinco, pouso autorizado, Papa Romeo Alfa Bravo Charlie")]
    [InlineData("pouso pista uno cinco, Papa Romeo Alfa Bravo Charlie")]
    public void The_readback_of_the_landing_clearance_is_not_a_new_request_to_land(string said)
    {
        _nlu.Interpret(said, Cleared).ShouldBeOfType<Readback>();
    }

    [Theory]
    [InlineData("reporte perna base, Papa Romeo Alfa Bravo Charlie")]
    [InlineData("reporte na final, Papa Romeo Alfa Bravo Charlie")]
    public void Repeating_a_report_instruction_is_a_readback_and_not_a_report(string said)
    {
        _nlu.Interpret(said, new FlightContext(Own, FlightPhase.OnDownwind, "SBCT", new ReportBase())).ShouldBeOfType<Readback>();
    }

    [Fact]
    public void Without_the_callsign_the_report_has_no_callsign()
    {
        _nlu.Interpret("perna base", Circuit).ShouldBe(new ReportBaseLeg(null));
    }

    [Theory]
    [InlineData("PRABC, pronto para decolagem")]
    [InlineData("PRABC, no ponto de espera da pista uno cinco")]
    public void The_words_of_the_departure_are_not_arrivals(string said)
    {
        _nlu.Interpret(said, new FlightContext(Own, FlightPhase.Taxiing, "SBCT", null)).ShouldNotBeOfType<RequestLanding>();
    }
}
