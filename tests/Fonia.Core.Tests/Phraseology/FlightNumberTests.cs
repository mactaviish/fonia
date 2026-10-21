using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;

namespace Fonia.Core.Tests.Phraseology;

// A flight number is said in different ways: digit by digit, in pairs, or as a whole with "mil" and hundreds. All are the same callsign.
public class FlightNumberTests
{
    [Theory]
    [InlineData("gol um dois três quatro")]
    [InlineData("gol doze trinta e quatro")]
    [InlineData("gol doze trinta quatro")]
    [InlineData("gol mil duzentos e trinta e quatro")]
    [InlineData("gol mil duzentos trinta e quatro")]
    [InlineData("gol mil e duzentos e trinta e quatro")]
    [InlineData("Gol 1234")]
    [InlineData("gol um mil duzentos e trinta e quatro")]
    public void The_same_flight_number_said_in_different_ways_is_the_same_callsign(string heard)
    {
        SpokenText.IsSameCallsign(heard, "GLO1234").ShouldBeTrue();
    }

    [Theory]
    [InlineData("azul dois mil e quinhentos", "AZU2500")]
    [InlineData("azul dois mil quinhentos e cinco", "AZU2505")]
    [InlineData("azul mil", "AZU1000")]
    [InlineData("azul trezentos e quarenta", "AZU340")]
    [InlineData("azul cem", "AZU100")]
    [InlineData("azul cento e vinte", "AZU120")]
    [InlineData("azul mil e quinze", "AZU1015")]
    [InlineData("tam novecentos e noventa e nove", "TAM999")]
    [InlineData("tam quatro zero um", "TAM401")]
    [InlineData("tam quarenta e um", "TAM41")]
    public void A_number_said_as_a_whole_is_read_by_its_value(string heard, string callsign)
    {
        SpokenText.IsSameCallsign(heard, callsign).ShouldBeTrue();
    }

    [Theory]
    [InlineData("gol mil duzentos e trinta e cinco")]
    [InlineData("gol mil duzentos e quarenta e quatro")]
    [InlineData("gol doze trinta e cinco")]
    [InlineData("gol duzentos e trinta e quatro")]
    public void Another_number_is_another_flight(string heard)
    {
        SpokenText.IsSameCallsign(heard, "GLO1234").ShouldBeFalse();
    }

    [Theory]
    [InlineData("gol mil duzentos e trinta e quatro, solicito táxi")]
    [InlineData("solo, gol doze trinta e quatro, pronto para o táxi")]
    [InlineData("gol um dois três quatro, solicito acionamento")]
    public void The_nlu_takes_the_flight_number_said_as_a_whole_for_the_aircraft(string said)
    {
        var intent = new RuleBasedNlu().Interpret(said, new FlightContext("GLO1234", FlightPhase.Parked, "SBXX", null));

        intent.Callsign.ShouldBe("GLO1234");
    }

    [Fact]
    public void The_registrations_and_frequencies_are_read_as_before()
    {
        SpokenText.IsSameCallsign("papa romeo alfa bravo charlie", "PRABC").ShouldBeTrue();
        SpokenCode.Decode("uno dois um decimal sete").ShouldBe("121.7");
    }
}
