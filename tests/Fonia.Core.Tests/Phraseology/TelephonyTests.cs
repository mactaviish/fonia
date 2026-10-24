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

    // A virtual airline: the user adds its telephony, and the flight is said and heard like any other company.
    private static readonly TelephonyTable Virtual = TelephonyTable.BuiltIn.With("WVR", "Wave Air");

    [Fact]
    public void A_company_the_user_added_is_said_as_its_telephony()
    {
        SpokenText.Callsign("WVR1234", telephony: Virtual).ShouldBe("WAVE AIR UNO DOIS TRÊS QUATRO");
    }

    [Fact]
    public void A_company_the_user_did_not_add_is_spelled_with_the_built_in_table()
    {
        SpokenText.Callsign("WVR1234").ShouldBe("WHISKEY VICTOR ROMEO UNO DOIS TRÊS QUATRO");
        TelephonyTable.BuiltIn.TryGet("WVR", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("wave air um dois três quatro")]
    [InlineData("Wave Air mil duzentos e trinta e quatro")]
    [InlineData("wave air 1234")]
    public void The_pilot_calling_the_company_the_user_added_is_the_same_aircraft(string heard)
    {
        SpokenText.IsSameCallsign(heard, "WVR1234", Virtual).ShouldBeTrue();
        SpokenText.IsSameCallsign(heard, "WVR1234").ShouldBeFalse();
    }

    [Fact]
    public void The_built_in_companies_stay_after_one_is_added()
    {
        SpokenText.Callsign("GLO1234", telephony: Virtual).ShouldBe("GOL UNO DOIS TRÊS QUATRO");
        SpokenText.IsSameCallsign("gol um dois três quatro", "GLO1234", Virtual).ShouldBeTrue();
        SpokenText.IsSameCallsign("gol transporte um dois três quatro", "GLO1234", Virtual).ShouldBeTrue();
    }

    [Fact]
    public void A_company_added_with_the_designator_of_another_replaces_it()
    {
        var table = TelephonyTable.BuiltIn.With("GLO", "Wave Air");

        SpokenText.Callsign("GLO1234", telephony: table).ShouldBe("WAVE AIR UNO DOIS TRÊS QUATRO");
        SpokenText.IsSameCallsign("gol um dois três quatro", "GLO1234", table).ShouldBeFalse();
        SpokenText.IsSameCallsign("wave air um dois três quatro", "GLO1234", table).ShouldBeTrue();
    }

    [Theory]
    [InlineData("WV", "Wave Air")]
    [InlineData("WVRX", "Wave Air")]
    [InlineData("W1R", "Wave Air")]
    [InlineData("WVR", "")]
    [InlineData("WVR", "  ")]
    public void A_designator_that_is_not_three_letters_or_a_telephony_without_words_is_refused(string designator, string spoken)
    {
        Should.Throw<ArgumentException>(() => TelephonyTable.BuiltIn.With(designator, spoken));
    }

    [Fact]
    public void The_flight_of_the_simulator_is_the_callsign_when_the_airline_is_one_the_user_added()
    {
        AirlineCallsign.From("Wave Air", "1234").ShouldBeNull();
        AirlineCallsign.From("Wave Air", "1234", Virtual).ShouldBe("WVR1234");
    }
}
