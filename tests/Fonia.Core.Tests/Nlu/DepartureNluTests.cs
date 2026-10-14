using Fonia.Core.Atc;
using Fonia.Core.Nlu;

namespace Fonia.Core.Tests.Nlu;

public class DepartureNluTests
{
    private const string Own = "PRABC";

    private static readonly FlightContext Parked = new(Own, FlightPhase.Parked, "SBCT", null);

    private static readonly FlightContext Taxiing = new(Own, FlightPhase.Taxiing, "SBCT", new TaxiClearance("15"));

    // Right after the Solo said "chame Torre ...", which waits for an optional readback.
    private static readonly FlightContext Handed = new(Own, FlightPhase.HandedToTower, "SBCT", new HandoffToTower(new Fonia.Core.Sim.Frequency(118550)));

    private static readonly FlightContext LineUp = new(Own, FlightPhase.LineUpCleared, "SBCT", new LineUpAndWait("15"));

    private static readonly FlightContext Takeoff = new(Own, FlightPhase.TakeoffCleared, "SBCT", new TakeoffClearance("15"));

    private readonly RuleBasedNlu _nlu = new();

    // Art. 126, I: "Torre Rio, TIB 5570, ponto de espera pista 17, pronto para partida."
    [Theory]
    [InlineData("Torre Curitiba, PRABC, ponto de espera pista uno cinco, pronto para partida", "15")]
    [InlineData("Torre Curitiba, Papa Romeo Alfa Bravo Charlie, no ponto de espera da pista uno cinco, pronto para partida", "15")]
    [InlineData("PRABC pronto para decolar", null)]
    [InlineData("PRABC, pronto para decolagem, pista uno cinco", "15")]
    [InlineData("Torre, PRABC, solicito decolagem pista 15", "15")]
    [InlineData("PRABC solicita decolagem", null)]
    [InlineData("Alfa Bravo Charlie, pronto para a partida", null)]
    [InlineData("PRABC, pronto para decolar, pista 1,5", "15")]
    public void The_pilot_ready_for_departure_is_understood_whether_or_not_the_holding_point_is_mentioned(string said, string? runway)
    {
        _nlu.Interpret(said, Handed).ShouldBe(new RequestDeparture(Own, runway));
        _nlu.Interpret(said, Taxiing).ShouldBe(new RequestDeparture(Own, runway));
    }

    [Fact]
    public void Ready_for_departure_after_the_line_up_is_the_same_request()
    {
        _nlu.Interpret("PRABC, pronto para decolar", LineUp).ShouldBe(new RequestDeparture(Own, null));
    }

    [Fact]
    public void Partida_is_the_engine_start_before_the_taxi_and_the_departure_after_it()
    {
        _nlu.Interpret("PRABC, pronto para partida", Taxiing).ShouldBeOfType<RequestDeparture>();
        _nlu.Interpret("PRABC, solicito partida dos motores", Taxiing).ShouldBeOfType<RequestStartup>();
        _nlu.Interpret("PRABC, solicito partida", Parked).ShouldBeOfType<RequestStartup>();
    }

    [Theory]
    [InlineData("PRABC alinhado")]
    [InlineData("PRABC, alinhado e pronto")]
    [InlineData("Torre Curitiba, Papa Romeo Alfa Bravo Charlie, alinhado")]
    public void The_pilot_who_says_they_are_lined_up_is_understood(string said)
    {
        _nlu.Interpret(said, LineUp).ShouldBe(new ReportLinedUp(Own, null));
    }

    [Fact]
    public void Lined_up_keeps_the_runway_if_one_was_said()
    {
        _nlu.Interpret("PRABC, alinhado pista uno cinco", LineUp).ShouldBe(new ReportLinedUp(Own, "15"));
    }

    [Theory]
    [InlineData("alinhe e mantenha pista uno cinco, PRABC")]
    [InlineData("alinhando e mantendo pista uno cinco, PRABC")]
    [InlineData("alinha e mantém, pista uno cinco, Alfa Bravo Charlie")]
    [InlineData("pista uno cinco, alinhe e mantenha, PRABC")]
    public void Repeating_the_line_up_instruction_is_a_readback(string said)
    {
        _nlu.Interpret(said, LineUp).ShouldBeOfType<Readback>();
    }

    [Theory]
    [InlineData("autorizado decolagem pista uno cinco, PRABC")]
    [InlineData("autorizado decolar, pista uno cinco, PRABC")]
    [InlineData("decolagem pista uno cinco, PRABC")]
    [InlineData("pista uno cinco, autorizado a decolar, Alfa Bravo Charlie")]
    public void Repeating_the_takeoff_clearance_is_a_readback(string said)
    {
        _nlu.Interpret(said, Takeoff).ShouldBeOfType<Readback>();
    }

    [Theory]
    [InlineData("PRABC decolando")]
    [InlineData("PRABC, iniciando a decolagem")]
    [InlineData("Alfa Bravo Charlie, decolei")]
    public void The_pilot_who_reports_taking_off_is_understood(string said)
    {
        _nlu.Interpret(said, Takeoff).ShouldBeOfType<ReportTakingOff>().Callsign.ShouldBe(Own);
    }

    [Fact]
    public void A_departure_request_without_the_callsign_has_none_so_the_engine_asks_for_it()
    {
        _nlu.Interpret("Torre Curitiba, pronto para partida", Handed).ShouldBe(new RequestDeparture(null, null));
    }
}
