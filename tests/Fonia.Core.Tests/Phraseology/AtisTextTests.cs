using Fonia.Core.Atc;
using Fonia.Core.Phraseology;
using Fonia.Core.Tests.TestSupport;
using Fonia.Core.Weather;

namespace Fonia.Core.Tests.Phraseology;

// The ATIS in the order of ICA 100-37, Art. 768, with the numbers said digit by digit.
public class AtisTextTests
{
    private static readonly AirportContext Airport = AirportContext.Create(TestAirports.WithSolo());

    private static string Atis(string raw)
    {
        MetarParser.TryParse(raw, out var metar).ShouldBeTrue();
        return AtisText.Combined(Airport.WithWeather(metar, keepRunway: false), metar);
    }

    [Theory]
    [InlineData(0, "ALFA")]
    [InlineData(1, "BRAVO")]
    [InlineData(12, "MIKE")]
    [InlineData(23, "X-RAY")]
    public void The_designator_is_the_letter_of_the_hour_of_the_observation_and_starts_again_at_midnight(int hour, string letter)
    {
        AtisText.Designator(hour).ShouldBe(letter);
    }

    [Fact]
    public void The_hour_is_the_only_thing_that_changes_the_letter()
    {
        MetarParser.TryParse("SBXX 151230Z 09008KT CAVOK 22/14 Q1018", out var first).ShouldBeTrue();
        MetarParser.TryParse("SBXX 151250Z 27010KT CAVOK 22/14 Q1010", out var second).ShouldBeTrue();

        AtisText.Designator(first).ShouldBe(AtisText.Designator(second));
    }

    [Fact]
    public void A_report_with_no_time_has_no_designator()
    {
        AtisText.Designator(new Metar("SBXX", null, null, null, false, "SBXX")).ShouldBeNull();
    }

    [Fact]
    public void A_clear_day_is_the_name_the_letter_the_time_the_runway_the_wind_cavok_the_temperature_and_the_qnh_in_that_order()
    {
        Atis("SBXX 151230Z 15008KT CAVOK 22/14 Q1018").ShouldBe(
            "Curitiba, informação MIKE, hora UNO DOIS TRÊS ZERO. PISTA UNO CINCO em uso. VENTO UNO CINCO ZERO GRAUS, OITO NÓS. CAVOK. " +
            "temperatura DOIS DOIS GRAUS. ajuste UNO ZERO UNO OITO. informe que recebeu informação MIKE.",
            StringCompareShould.IgnoreCase);
    }

    [Fact]
    public void With_clouds_and_weather_the_visibility_comes_first_then_the_weather_and_then_the_clouds()
    {
        var text = Atis("SBXX 151200Z 15008KT 3000 -SHRA BKN020 SCT040CB 21/19 Q1010");

        text.ShouldContain("visibilidade TRÊS MIL METROS. pancada de chuva fraca. MEIA OITAVOS DOIS MIL PÉS. QUATRO OITAVOS CUMULONIMBOS QUATRO MIL PÉS. temperatura DOIS UNO GRAUS");
    }

    [Theory]
    [InlineData("4500", "visibilidade QUATRO CINCO ZERO ZERO METROS")]
    [InlineData("7000", "visibilidade SETE QUILÔMETROS")]
    [InlineData("9999", "visibilidade maior do que DEZ QUILÔMETROS")]
    public void The_visibility_is_in_meters_up_to_five_thousand_then_in_kilometers_and_from_ten_kilometers_it_is_more_than_that(string metarVisibility, string said)
    {
        Atis($"SBXX 151200Z 15008KT {metarVisibility} SCT040 21/19 Q1010").ShouldContain(said);
    }

    [Fact]
    public void Under_cavok_nothing_is_said_of_the_visibility_the_weather_or_the_clouds()
    {
        var text = Atis("SBXX 151200Z 15008KT CAVOK 21/19 Q1010");

        text.ShouldNotContain("visibilidade");
        text.ShouldNotContain("OITAVOS");
    }

    [Fact]
    public void A_negative_temperature_is_menos()
    {
        Atis("SBXX 151200Z 15008KT CAVOK M02/M05 Q1010").ShouldContain("temperatura MENOS ZERO DOIS GRAUS");
    }

    [Fact]
    public void A_report_with_less_leaves_out_what_it_does_not_have_but_always_says_the_qnh_it_has()
    {
        MetarParser.TryParse("SBXX 151200Z 15008KT CAVOK 21/19 Q1010", out var metar).ShouldBeTrue();
        var bare = new Metar("SBXX", null, null, 1013, false, "SBXX");

        var text = AtisText.Combined(AirportContext.Create(TestAirports.WithSolo()), bare);

        text.ShouldContain("ajuste UNO ZERO UNO TRÊS");
        text.ShouldNotContain("VENTO");
        text.ShouldNotContain("temperatura");
    }
}
