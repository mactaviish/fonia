using Fonia.Core.Phraseology;
using Fonia.Core.Sim;

namespace Fonia.Core.Tests.Phraseology;

// The simulator gives the airline as the telephony set for the radio and the flight number apart: together they are the callsign of the flight.
public class AirlineCallsignTests
{
    [Theory]
    [InlineData("Gol", "1234", "GLO1234")]
    [InlineData("GOL", " 1234 ", "GLO1234")]
    [InlineData("Gol Transporte", "1234", "GLO1234")]
    [InlineData("Azul", "4567", "AZU4567")]
    [InlineData("Air Canada", "91", "ACA91")]
    [InlineData("tam", "3373", "TAM3373")]
    [InlineData("Gol", "12A", "GLO12A")]
    public void The_airline_and_the_flight_number_are_the_callsign(string airline, string number, string expected)
    {
        AirlineCallsign.From(airline, number).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "1234")]
    [InlineData("", "1234")]
    [InlineData("Gol", null)]
    [InlineData("Gol", "")]
    [InlineData("Gol", "ABC")]
    [InlineData("Empresa Que Nao Existe", "1234")]
    [InlineData("Gol Linhas Aereas Brasileiras", "1234")]
    public void Without_a_known_airline_or_a_number_there_is_no_airline_callsign(string? airline, string? number)
    {
        AirlineCallsign.From(airline, number).ShouldBeNull();
    }

    private static AircraftState State(string? airline, string? number) =>
        new(DateTimeOffset.UtcNow, -25.5, -49.1, 3000, 0, 0, 110, true, null, null, 1200, "PR-ABC", "B738", airline, number);

    [Fact]
    public void The_callsign_of_the_aircraft_is_the_flight_when_the_simulator_has_it_and_the_registration_otherwise()
    {
        State("Gol", "1234").Callsign.ShouldBe("GLO1234");
        State(null, null).Callsign.ShouldBe("PR-ABC");
        State("Gol", null).Callsign.ShouldBe("PR-ABC");
        State("Unknown", "12").Callsign.ShouldBe("PR-ABC");
    }

    [Fact]
    public void The_flight_is_said_and_heard_by_its_telephony()
    {
        var callsign = State("Gol", "1234").Callsign;

        SpokenText.Callsign(callsign).ShouldBe("GOL UNO DOIS TRÊS QUATRO");
        SpokenText.IsSameCallsign("gol mil duzentos e trinta e quatro", callsign).ShouldBeTrue();
    }
}
