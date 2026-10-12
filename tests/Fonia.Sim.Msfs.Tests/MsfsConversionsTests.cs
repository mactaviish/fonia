using Fonia.Core.Sim;

namespace Fonia.Sim.Msfs.Tests;

public class MsfsConversionsTests
{
    [Theory]
    [InlineData(122_800_000.0, 122800)]
    [InlineData(118_000_000.0, 118000)]
    [InlineData(124_850_400.0, 124850)] // rounds to the nearest kHz
    [InlineData(136_975_000.0, 136975)]
    public void A_frequency_in_hz_becomes_whole_khz(double hz, int expectedKhz)
    {
        MsfsConversions.ToFrequency(hz).ShouldBe(new Frequency(expectedKhz));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-118_000_000.0)]
    [InlineData(499.0)] // rounds to 0 kHz
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e30)]
    public void A_frequency_that_is_not_usable_is_null(double hz)
    {
        MsfsConversions.ToFrequency(hz).ShouldBeNull();
    }

    [Fact]
    public void A_radio_needs_a_valid_active_frequency()
    {
        MsfsConversions.ToRadio(122_800_000, 124_850_000).ShouldBe(new RadioState(new Frequency(122800), new Frequency(124850)));
        MsfsConversions.ToRadio(0, 124_850_000).ShouldBeNull();
        MsfsConversions.ToRadio(0, 0).ShouldBeNull();
    }

    [Fact]
    public void A_radio_without_a_valid_standby_uses_the_active_frequency_for_both()
    {
        MsfsConversions.ToRadio(122_800_000, 0).ShouldBe(new RadioState(new Frequency(122800), new Frequency(122800)));
    }

    [Theory]
    [InlineData(0x1200, 1200)]
    [InlineData(0x7700, 7700)]
    [InlineData(0x7000, 7000)] // 28672: the value the spike logged for squawk 7000
    [InlineData(0x1234, 1234)] // 4660: the value the spike logged for squawk 1234
    [InlineData(0x0000, 0)]
    [InlineData(0x0007, 7)]
    public void A_bco16_transponder_code_becomes_its_decimal_digits(int bco16, int expectedSquawk)
    {
        MsfsConversions.ToSquawk(bco16).ShouldBe(expectedSquawk);
    }

    [Theory]
    [InlineData(0x1289)] // digits 8 and 9 cannot be dialed
    [InlineData(0x000A)] // not a BCD digit
    [InlineData(-1)]
    [InlineData(0x10000)] // more than four digits
    public void An_invalid_transponder_code_is_zero(int bco16)
    {
        MsfsConversions.ToSquawk(bco16).ShouldBe(0);
    }

    [Theory]
    [InlineData("ATCCOM.AC_MODEL_A320.0.text", "Fenix A320 - Wave Air", "A320")]
    [InlineData("ATCCOM.AC_MODEL_B738.0.text", "", "B738")]
    [InlineData("C25C", "Cessna CJ4 Citation Asobo", "C25C")]
    [InlineData("  C172  ", "Cessna 172", "C172")]
    [InlineData("", "Cessna 152 Asobo", "Cessna 152 Asobo")] // falls back to the title
    [InlineData("  ", "  ", "")]
    public void The_aircraft_type_comes_from_the_model_with_a_title_fallback(string model, string title, string expected)
    {
        MsfsConversions.ToAircraftType(model, title).ShouldBe(expected);
    }

    [Fact]
    public void The_registration_is_trimmed()
    {
        MsfsConversions.ToRegistration("  PR-FRA ").ShouldBe("PR-FRA");
        MsfsConversions.ToRegistration(string.Empty).ShouldBe(string.Empty);
    }
}
