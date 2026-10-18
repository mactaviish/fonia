using Fonia.Core.Weather;

namespace Fonia.Core.Tests.Weather;

// The groups of a METAR that only the ATIS says: the clouds, the present weather, CAVOK and the time of the observation.
public class MetarSkyTests
{
    private static Metar Parse(string raw)
    {
        MetarParser.TryParse(raw, out var metar).ShouldBeTrue();
        return metar;
    }

    [Fact]
    public void The_layers_of_clouds_are_read_with_their_cover_in_eighths_and_their_base_in_feet()
    {
        var metar = Parse("SBXX 151230Z 09008KT 9999 FEW020 BKN040 OVC100 22/14 Q1018");

        metar.Clouds.ShouldBe([new CloudLayer(2, 2000), new CloudLayer(6, 4000), new CloudLayer(8, 10000)]);
    }

    [Fact]
    public void Only_cumulonimbus_is_marked_as_such()
    {
        var metar = Parse("SBXX 151230Z 09008KT 4000 SCT015CB BKN025TCU 22/14 Q1018");

        metar.Clouds!.Select(c => c.Cumulonimbus).ShouldBe([true, false]);
    }

    [Fact]
    public void A_sky_without_clouds_has_no_layers()
    {
        Parse("SBXX 151230Z 09008KT 9999 NSC 22/14 Q1018").Clouds.ShouldBeEmpty();
    }

    [Fact]
    public void Cavok_is_known_and_the_visibility_is_ten_kilometers_or_more()
    {
        var metar = Parse("SBXX 151230Z 09008KT CAVOK 22/14 Q1018");

        metar.Cavok.ShouldBeTrue();
        metar.VisibilityMeters.ShouldBe(9999);
    }

    [Fact]
    public void The_time_of_the_observation_is_the_hour_and_the_minute_of_the_time_group()
    {
        var metar = Parse("SBXX 151230Z 09008KT CAVOK 22/14 Q1018");

        metar.ObservedHour.ShouldBe(12);
        metar.ObservedMinute.ShouldBe(30);
    }

    [Fact]
    public void The_present_weather_is_read_and_the_clouds_and_the_temperature_are_not_taken_for_it()
    {
        var metar = Parse("SBXX 151230Z 09008KT 3000 -SHRA BR BKN020 22/14 Q1018 RMK RA");

        metar.PresentWeather.ShouldBe(["-SHRA", "BR"]);
    }

    [Fact]
    public void Weather_in_the_vicinity_is_not_weather_at_the_aerodrome()
    {
        Parse("SBXX 151230Z 09008KT 9999 VCTS SCT030 22/14 Q1018").PresentWeather.ShouldBeEmpty();
    }
}
