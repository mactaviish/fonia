using Fonia.Core.Atc;
using Fonia.Core.Nlu;

namespace Fonia.Core.Tests.Nlu;

public class HoldingShortNluTests
{
    private const string Own = "PRABC";

    private static readonly FlightContext Parked = new(Own, FlightPhase.Parked, "SBCT", null);

    private static readonly FlightContext Taxiing = new(Own, FlightPhase.Taxiing, "SBCT", new TaxiClearance("15"));

    private readonly RuleBasedNlu _nlu = new();

    [Theory]
    [InlineData("Solo Curitiba, PRABC, no ponto de espera da pista 15", "15")]
    [InlineData("PRABC no ponto de espera da pista um cinco", "15")]
    [InlineData("PRABC, em ponto de espera pista dois três esquerda", "23L")]
    [InlineData("Papa Romeo Alfa Bravo Charlie, no ponto de espera da 33", "33")]
    [InlineData("PRABC no ponto de espera, pronto", null)]
    [InlineData("alfa bravo charlie, no ponto de espera", null)]
    [InlineData("PRABC chegando no ponto de espera da pista 15, pronto para decolagem", "15")]
    [InlineData("Solo, PRABC, no ponto de espera pista 1,5", "15")]
    public void The_pilot_who_reports_being_at_the_holding_point_is_understood_even_with_a_readback_waiting(string said, string? runway)
    {
        _nlu.Interpret(said, Taxiing).ShouldBe(new ReportHoldingShort(Own, runway));
    }

    [Theory]
    [InlineData("autorizado táxi para o ponto de espera da pista um cinco, PRABC")]
    [InlineData("táxi para o ponto de espera da pista um cinco, PRABC")]
    [InlineData("PRABC, ponto de espera, pista um cinco")]
    [InlineData("para o ponto de espera da 15, PRABC")]
    public void Repeating_the_taxi_clearance_is_still_a_readback(string said)
    {
        _nlu.Interpret(said, Taxiing).ShouldBeOfType<Readback>();
    }

    [Fact]
    public void Asking_for_taxi_to_the_holding_point_is_a_request_not_a_report()
    {
        _nlu.Interpret("PRABC solicito táxi para o ponto de espera da pista 15", Parked).ShouldBe(new RequestTaxi(Own, "15"));
    }

    [Fact]
    public void A_report_without_the_callsign_has_none_so_the_engine_asks_for_it()
    {
        _nlu.Interpret("no ponto de espera da pista 15", Taxiing).ShouldBe(new ReportHoldingShort(null, "15"));
    }
}
