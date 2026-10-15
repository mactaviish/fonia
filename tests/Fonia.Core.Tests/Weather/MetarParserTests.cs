using Fonia.Core.Sim;
using Fonia.Core.Weather;

namespace Fonia.Core.Tests.Weather;

public class MetarParserTests
{
    [Fact]
    public void A_brazilian_metar_gives_the_wind_the_temperature_and_the_qnh()
    {
        var ok = MetarParser.TryParse("SBCT 151200Z 09008KT 9999 SCT030 22/14 Q1018", out var metar);

        ok.ShouldBeTrue();
        metar.Station.ShouldBe("SBCT");
        metar.Wind.ShouldBe(new Wind(90, 8));
        metar.TemperatureCelsius.ShouldBe(22);
        metar.QnhHectopascals.ShouldBe(1018);
        metar.Raw.ShouldBe("SBCT 151200Z 09008KT 9999 SCT030 22/14 Q1018");
    }

    [Fact]
    public void Gusts_are_read()
    {
        MetarParser.TryParse("SBGR 151200Z 18015G28KT CAVOK 25/12 Q1015", out var metar).ShouldBeTrue();

        metar.Wind.ShouldBe(new Wind(180, 15, 28));
    }

    [Theory]
    [InlineData("SBGL 151200Z 00000KT CAVOK 27/20 Q1012")]
    public void Calm_is_a_wind_of_no_speed(string raw)
    {
        MetarParser.TryParse(raw, out var metar).ShouldBeTrue();

        metar.Wind.ShouldBe(new Wind(0, 0));
    }

    [Fact]
    public void A_variable_wind_has_no_direction_to_say_and_is_left_out()
    {
        MetarParser.TryParse("SBPA 151200Z VRB03KT 9999 FEW020 18/11 Q1021", out var metar).ShouldBeTrue();

        metar.Wind.ShouldBeNull();
        metar.VariableWind.ShouldBeTrue();
        metar.QnhHectopascals.ShouldBe(1021);
    }

    [Fact]
    public void The_altimeter_in_inches_is_turned_into_hectopascals()
    {
        MetarParser.TryParse("KJFK 151251Z 28012KT 10SM FEW250 15/06 A2992", out var metar).ShouldBeTrue();

        metar.QnhHectopascals.ShouldBe(1013);
        metar.Wind.ShouldBe(new Wind(280, 12));
    }

    [Theory]
    [InlineData("SBBR 151200Z 12005KT 9999 FEW030 M02/M05 Q1020", -2)]
    [InlineData("SBBR 151200Z 12005KT 9999 FEW030 05/M01 Q1020", 5)]
    public void A_negative_temperature_has_an_m(string raw, int expected)
    {
        MetarParser.TryParse(raw, out var metar).ShouldBeTrue();

        metar.TemperatureCelsius.ShouldBe(expected);
    }

    [Fact]
    public void Wind_in_meters_per_second_is_turned_into_knots()
    {
        MetarParser.TryParse("UUEE 151200Z 25005MPS 9999 BKN040 10/05 Q1010", out var metar).ShouldBeTrue();

        metar.Wind.ShouldBe(new Wind(250, 10));
    }

    [Fact]
    public void The_prefix_the_type_and_the_remarks_do_not_get_in_the_way()
    {
        MetarParser.TryParse("METAR COR SBCT 151200Z AUTO 36010KT 9999 NCD 20/10 Q1015 RMK PK WND 36020/1150 22/14 Q1000", out var metar).ShouldBeTrue();

        metar.Station.ShouldBe("SBCT");
        metar.Wind.ShouldBe(new Wind(360, 10));
        metar.TemperatureCelsius.ShouldBe(20);
        metar.QnhHectopascals.ShouldBe(1015);
    }

    [Fact]
    public void What_the_metar_lacks_is_left_empty()
    {
        MetarParser.TryParse("SBFZ 151200Z 12010KT 9999", out var metar).ShouldBeTrue();

        metar.QnhHectopascals.ShouldBeNull();
        metar.TemperatureCelsius.ShouldBeNull();
        metar.Wind.ShouldBe(new Wind(120, 10));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a metar")]
    [InlineData("SBCT")]
    public void Something_that_is_not_a_metar_is_refused(string raw)
    {
        MetarParser.TryParse(raw, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_wind_direction_of_360_is_north_and_one_above_it_is_not_a_wind()
    {
        MetarParser.TryParse("SBCT 151200Z 99908KT 9999 22/14 Q1018", out var metar).ShouldBeTrue();

        metar.Wind.ShouldBeNull();
    }

    [Theory]
    [InlineData("SBCT 151200Z 09008KT 9999 SCT030 22/14 Q1018", 9999)]
    [InlineData("SBCT 151200Z 09008KT 3000 BR BKN008 22/14 Q1018", 3000)]
    [InlineData("SBCT 151200Z 09008KT 0700 FG VV001 22/14 Q1018", 700)]
    [InlineData("SBCT 151200Z 09008KT CAVOK 22/14 Q1018", 9999)]
    [InlineData("SBCT 151200Z VRB03KT 8000 22/14 Q1018", 8000)]
    public void The_visibility_in_meters_is_read(string raw, int expected)
    {
        MetarParser.TryParse(raw, out var metar).ShouldBeTrue();

        metar.VisibilityMeters.ShouldBe(expected);
    }

    [Fact]
    public void A_metar_without_a_visibility_in_meters_leaves_it_empty()
    {
        MetarParser.TryParse("KJFK 151251Z 28012KT 10SM FEW250 15/06 A2992", out var metar).ShouldBeTrue();

        metar.VisibilityMeters.ShouldBeNull();
    }
}
