using Fonia.Core.Phraseology;

namespace Fonia.Core.Tests.Phraseology;

public class SpokenTextTests
{
    [Theory]
    [InlineData("0", "ZERO")]
    [InlineData("118.1", "UNO UNO OITO DECIMAL UNO")]
    [InlineData("1017", "UNO ZERO UNO SETE")]
    [InlineData("4321", "QUATRO TRÊS DOIS UNO")]
    [InlineData("09", "ZERO NOVE")]
    [InlineData("6", "MEIA")]
    [InlineData("23", "DOIS TRÊS")]
    public void Digits_are_said_one_by_one_as_the_manual_says(string digits, string expected)
    {
        SpokenText.Digits(digits).ShouldBe(expected);
    }

    [Theory]
    [InlineData("1", "UMA")]
    [InlineData("21", "DOIS UMA")]
    [InlineData("2", "DUAS")]
    [InlineData("12", "UNO DUAS")]
    [InlineData("3", "TRÊS")]
    public void The_last_digit_agrees_with_a_feminine_word_that_follows(string digits, string expected)
    {
        SpokenText.Digits(digits, feminine: true).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12a")]
    [InlineData("1-2")]
    public void Digits_refuses_what_is_not_a_number(string digits)
    {
        Should.Throw<ArgumentException>(() => SpokenText.Digits(digits));
    }

    [Fact]
    public void The_alphabet_has_the_manuals_spelling_for_all_26_letters()
    {
        SpokenText.Spell("ABCDEFGHIJKLMNOPQRSTUVWXYZ").ShouldBe(
            "ALFA BRAVO CHARLIE DELTA ECHO FOXTROT GOLF HOTEL INDIA JULIETT KILO LIMA MIKE NOVEMBER OSCAR PAPA QUEBEC ROMEO SIERRA TANGO UNIFORM VICTOR WHISKEY X-RAY YANKEE ZULU");
    }

    [Fact]
    public void Romeo_is_romeo_and_not_the_old_romeu()
    {
        SpokenText.Spell("R").ShouldBe("ROMEO");
    }

    [Fact]
    public void Spell_says_digits_as_digits_and_ignores_dashes_and_spaces()
    {
        SpokenText.Spell("pr-abc").ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE");
        SpokenText.Spell("N12").ShouldBe("NOVEMBER UNO DOIS");
    }

    [Theory]
    [InlineData("PR-ABC", "PAPA ROMEO ALFA BRAVO CHARLIE")]
    [InlineData("PTAAP", "PAPA TANGO ALFA ALFA PAPA")]
    [InlineData("GLOGOW", "GOL GOLF OSCAR WHISKEY")]
    [InlineData("TAM3373", "TAM TRÊS TRÊS SETE TRÊS")]
    [InlineData("FAB2114", "FORÇA AÉREA DOIS UNO UNO QUATRO")]
    public void Callsigns_follow_the_manuals_examples(string callsign, string expected)
    {
        SpokenText.Callsign(callsign).ShouldBe(expected);
    }

    [Fact]
    public void A_registration_can_be_cut_to_its_last_three_characters_but_a_company_callsign_cannot()
    {
        SpokenText.Callsign("PTABC", abbreviate: true).ShouldBe("ALFA BRAVO CHARLIE");
        SpokenText.Callsign("TAM3373", abbreviate: true).ShouldBe("TAM TRÊS TRÊS SETE TRÊS");
        SpokenText.Callsign("GLOGOW", abbreviate: true).ShouldBe("GOL GOLF OSCAR WHISKEY");
    }

    [Theory]
    [InlineData("PRABC", "PRABC", true)]
    [InlineData("PR-ABC", "PRABC", true)]
    [InlineData("pr abc", "PRABC", true)]
    [InlineData("papa romeo alfa bravo charlie", "PRABC", true)]
    [InlineData("alfa bravo charlie", "PRABC", true)]
    [InlineData("ABC", "PRABC", true)]
    [InlineData("alfa bravo delta", "PRABC", false)]
    [InlineData("PRABD", "PRABC", false)]
    [InlineData("AB", "PRABC", false)]
    [InlineData("tam tres tres sete tres", "TAM3373", true)]
    [InlineData("tam tres sete tres", "TAM3373", false)]
    [InlineData("373", "TAM3373", false)]
    [InlineData("gol golf oscar whiskey", "GLOGOW", true)]
    [InlineData("GLO GOW", "GLOGOW", true)]
    [InlineData("força aérea dois uno uno quatro", "FAB2114", true)]
    [InlineData("", "PRABC", false)]
    public void The_pilot_may_say_the_callsign_in_full_spelled_or_abbreviated(string heard, string own, bool expected)
    {
        SpokenText.IsSameCallsign(heard, own).ShouldBe(expected);
    }

    // What the speech recognizer writes for "papa romeo alfa bravo charlie" when it is not sure of a word.
    [Theory]
    [InlineData("Papá Romeo, Alfa, Bravo Charly", true)]
    [InlineData("papa romeu alfa bravo charli", true)]
    [InlineData("papa romeo alfa bravo chalie", true)]
    [InlineData("papa romeo alfa bravo charllie", true)]
    [InlineData("papa romeo alpha bravo xarli", true)]
    [InlineData("papa romeoalfa bravocharlie", true)]
    [InlineData("alfa bravo charly", true)]
    [InlineData("papa romeo alfa bravo delta", false)]
    [InlineData("papa romeo alfa bravo", false)]
    [InlineData("papa romeo alfa tango charlie", false)]
    public void Phonetic_words_the_recognizer_misspells_by_a_letter_or_glues_together_are_still_read(string heard, bool expected)
    {
        SpokenText.IsSameCallsign(heard, "PRABC").ShouldBe(expected);
    }

    // Short phonetic words are read only when exact: "tanto" is one letter from "tango", "motel" from "hotel".
    [Theory]
    [InlineData("tanto", "PRATC")]
    [InlineData("motel", "PRAHC")]
    [InlineData("banco", "PRABC")]
    [InlineData("brava", "PRABC")]
    public void Ordinary_words_close_to_a_short_phonetic_word_are_not_taken_for_a_letter(string word, string own)
    {
        SpokenText.IsSameCallsign($"papa romeo alfa {word} charlie", own).ShouldBeFalse();
    }
}
