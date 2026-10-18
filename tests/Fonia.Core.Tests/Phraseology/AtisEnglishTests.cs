using Fonia.Core.Atc;
using Fonia.Core.Phraseology;
using Fonia.Core.Speech;
using Fonia.Core.Tests.TestSupport;
using Fonia.Core.Weather;

namespace Fonia.Core.Tests.Phraseology;

// The second half of the ATIS: the same items in English, in words the voice has phonemes for.
public class AtisEnglishTests
{
    private static readonly AirportContext Airport = AirportContext.Create(TestAirports.WithSolo());

    private static IReadOnlyList<string> Sentences(string raw)
    {
        MetarParser.TryParse(raw, out var metar).ShouldBeTrue();
        return AtisEnglish.Sentences(Airport.WithWeather(metar, keepRunway: false), metar);
    }

    [Fact]
    public void A_clear_day_is_the_name_the_runway_the_wind_cavok_the_temperature_and_the_qnh_in_that_order()
    {
        Sentences("SBXX 151230Z 15008KT CAVOK 22/14 Q1018").ShouldBe(
        [
            "Curitiba information mike, time one two three zero zulu",
            "runway one five in use",
            "wind one five zero degrees eight knots",
            "CAVOK",
            "temperature two two",
            "QNH one zero one eight",
            "advise on initial contact you have information mike",
        ],
        ignoreOrder: false,
        customMessage: null);
    }

    [Fact]
    public void Weather_visibility_and_clouds_come_in_words()
    {
        var text = string.Join(". ", Sentences("SBXX 151200Z 15008G18KT 3000 -SHRA BKN020 SCT040CB M01/M03 Q1010"));

        text.ShouldContain("wind one five zero degrees eight knots, gusting one eight");
        text.ShouldContain("visibility three thousand meters. light shower of rain. six oktas two thousand feet. four oktas cumulonimbus four thousand feet");
        text.ShouldContain("temperature minus zero one");
    }

    [Theory]
    [InlineData("4500", "visibility four thousand five hundred meters")]
    [InlineData("4550", "visibility four five five zero meters")]
    [InlineData("7000", "visibility seven kilometers")]
    [InlineData("9999", "visibility more than ten kilometers")]
    public void The_visibility_follows_the_same_limits_as_the_portuguese_one(string metarVisibility, string said)
    {
        string.Join(". ", Sentences($"SBXX 151200Z 15008KT {metarVisibility} SCT040 21/19 Q1010")).ShouldContain(said);
    }

    // Every word but the name of the airport is on the list of the English phonemizer, for any weather the report can have.
    [Fact]
    public void Every_word_is_one_the_voice_knows_but_the_name_of_the_airport()
    {
        string[] reports =
        [
            "SBXX 151230Z 15008KT CAVOK 22/14 Q1018",
            "SBXX 151200Z 00000KT 3000 TSRA FEW010 SCT020CB BKN030 OVC100 M01/M03 Q1000",
            "SBXX 152300Z 27015G25KT 800 +RA FG BR HZ FU DZ GR OVC002 05/04 Q0990",
            "SBXX 150000Z 36005KT 6000 BKN015 18/17 Q1030",
        ];

        var unknown = reports
            .SelectMany(Sentences)
            .SelectMany(sentence => sentence.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
            .Where(word => !EnglishPhonemizer.Knows(word) && !word.Equals("Curitiba", StringComparison.OrdinalIgnoreCase) && !word.Equals("CAVOK", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        unknown.ShouldBeEmpty();
    }

    // CAVOK is said the Brazilian way, "cavóc", in the Portuguese half and in the English one.
    // MCA 100-16, Art. 32: in English the round hundreds and thousands are said with the words (the Portuguese says every digit).
    [Fact]
    public void A_cloud_base_of_two_thousand_three_hundred_feet_is_said_with_thousand_and_hundred()
    {
        string.Join(". ", Sentences("SBXX 151200Z 15008KT 9999 BKN023 21/19 Q1010")).ShouldContain("six oktas two thousand three hundred feet");
        string.Join(". ", Sentences("SBXX 151200Z 15008KT 9999 SCT007 21/19 Q1010")).ShouldContain("four oktas seven hundred feet");
    }

    [Fact]
    public void Cavok_is_cavoc_in_both_languages()
    {
        var cavoc = Pronunciation.ToPhonemes("cavóc");

        Pronunciation.ToPhonemes("CAVOK").ShouldBe(cavoc);
        EnglishPhonemizer.Phonemize("CAVOK").ShouldBe(cavoc);
    }

    [Fact]
    public void The_phonemizer_gives_phonemes_for_a_word_of_the_list_and_reads_a_name_by_the_portuguese_rules()
    {
        EnglishPhonemizer.Phonemize("one two").ShouldBe("wˈʌn tˈu");
        EnglishPhonemizer.Phonemize("Curitiba").ShouldBe(Pronunciation.ToPhonemes("Curitiba"));
        EnglishPhonemizer.Phonemize("one, two.").ShouldBe("wˈʌn, tˈu.");
    }
}
