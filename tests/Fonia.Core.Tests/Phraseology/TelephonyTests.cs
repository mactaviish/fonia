using Fonia.Core.Phraseology;

namespace Fonia.Core.Tests.Phraseology;

// The telephony of the companies (data/telephony.csv): said for the designator of a callsign, read back for the name the pilot says.
public class TelephonyTests
{
    [Theory]
    [InlineData("GLO1234", "GOL UNO DOIS TRÊS QUATRO")]
    [InlineData("AZU4567", "AZUL QUATRO CINCO MEIA SETE")]
    [InlineData("TAM3373", "TAM TRÊS TRÊS SETE TRÊS")]
    [InlineData("ACA91", "AIR CANADA NOVE UNO")]
    [InlineData("TAP8081", "AIR PORTUGAL OITO ZERO OITO UNO")]
    [InlineData("BAW247", "SPEEDBIRD DOIS QUATRO SETE")]
    [InlineData("FAB2401", "FORÇA AÉREA DOIS QUATRO ZERO UNO")]
    public void The_designator_of_a_company_is_said_as_its_telephony(string callsign, string expected)
    {
        SpokenText.Callsign(callsign).ShouldBe(expected);
    }

    [Theory]
    [InlineData("gol um dois três quatro", "GLO1234")]
    [InlineData("azul quatro cinco meia sete", "AZU4567")]
    [InlineData("air canada nove um", "ACA91")]
    [InlineData("air portugal oito zero oito um", "TAP8081")]
    [InlineData("lan cargo cinco", "LCO5")]
    [InlineData("lan chile cinco", "LAN5")]
    public void The_telephony_the_pilot_says_is_the_company_designator_even_with_more_than_one_word(string heard, string callsign)
    {
        SpokenText.IsSameCallsign(heard, callsign).ShouldBeTrue();
    }

    [Fact]
    public void A_company_that_is_not_the_one_of_the_callsign_is_not_the_same_aircraft()
    {
        SpokenText.IsSameCallsign("azul um dois três quatro", "GLO1234").ShouldBeFalse();
        SpokenText.IsSameCallsign("air canada um dois três quatro", "GLO1234").ShouldBeFalse();
    }

    [Fact]
    public void A_designator_the_table_does_not_have_is_spelled_as_before()
    {
        SpokenText.Callsign("XYZ123").ShouldBe("X-RAY YANKEE ZULU UNO DOIS TRÊS");
    }
}
