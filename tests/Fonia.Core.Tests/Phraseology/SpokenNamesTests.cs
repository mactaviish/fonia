using Fonia.Core.Phraseology;
using Fonia.Core.Sim;

namespace Fonia.Core.Tests.Phraseology;

public class SpokenNamesTests
{
    [Theory]
    [InlineData("09", "PISTA ZERO NOVE")]
    [InlineData("9", "PISTA ZERO NOVE")]
    [InlineData("10", "PISTA UNO ZERO")]
    [InlineData("36", "PISTA TRÊS MEIA")]
    [InlineData("23L", "PISTA DOIS TRÊS ESQUERDA")]
    [InlineData("29r", "PISTA DOIS NOVE DIREITA")]
    [InlineData("15C", "PISTA UNO CINCO CENTRO")]
    public void A_runway_is_said_with_its_number_digit_by_digit_and_its_side(string designator, string expected)
    {
        SpokenText.Runway(designator).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("H1")]
    [InlineData("37")]
    [InlineData("00")]
    public void Only_a_runway_designator_can_be_said_as_a_runway(string designator)
    {
        Should.Throw<ArgumentException>(() => SpokenText.Runway(designator));
    }

    [Theory]
    [InlineData(118100, "UNO UNO OITO DECIMAL UNO")]
    [InlineData(121500, "UNO DOIS UNO DECIMAL CINCO")]
    [InlineData(118275, "UNO UNO OITO DECIMAL DOIS SETE CINCO")]
    [InlineData(118000, "UNO UNO OITO DECIMAL ZERO")]
    [InlineData(118005, "UNO UNO OITO DECIMAL ZERO ZERO CINCO")]
    [InlineData(132650, "UNO TRÊS DOIS DECIMAL MEIA CINCO")]
    public void A_frequency_is_said_in_mhz_without_the_trailing_zeros(int kHz, string expected)
    {
        SpokenText.Frequency(new Frequency(kHz)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1017, "UNO ZERO UNO SETE")]
    [InlineData(1013, "UNO ZERO UNO TRÊS")]
    [InlineData(1000, "UNO MIL")]
    [InlineData(999, "NOVE NOVE NOVE")]
    [InlineData(1001, "UNO ZERO ZERO UNO")]
    public void The_altimeter_setting_is_said_digit_by_digit_and_one_thousand_is_uno_mil(int hPa, string expected)
    {
        SpokenText.Qnh(hPa).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(899)]
    [InlineData(1100)]
    public void An_altimeter_setting_outside_any_real_pressure_is_refused(int hPa)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => SpokenText.Qnh(hPa));
    }
}
