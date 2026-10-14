using Fonia.Core.Speech;

namespace Fonia.Core.Tests.Speech;

/// <summary>One rule of the spelling at a time, so that a wrong phoneme points at the rule that made it.</summary>
public class PortugueseRulesTests
{
    private static string Say(string word) => PortuguesePhonemizer.PhonemizeWord(word);

    [Theory]
    [InlineData("tia", "tʃˈiæ")]
    [InlineData("dia", "dʒˈiæ")]
    [InlineData("tema", "tˈemæ")]
    [InlineData("noite", "nˈoɪtʃy")]
    [InlineData("tarde", "tˈaɾədʒy")]
    public void T_and_d_turn_soft_before_i_and_before_a_final_e(string word, string expected)
    {
        Say(word).ShouldBe(expected);
    }

    [Theory]
    [InlineData("filho", "fˈiljʊ")]
    [InlineData("vinho", "vˈiɲʊ")]
    [InlineData("chave", "ʃˈavy")]
    [InlineData("quilo", "kˈilʊ")]
    [InlineData("guerra", "ɡˈexæ")]
    [InlineData("gato", "ɡˈatʊ")]
    [InlineData("gente", "ʒˈeɪŋtʃy")]
    [InlineData("quatro", "kwˈatrʊ")]
    [InlineData("água", "ˈaɡwæ")]
    public void Digraphs_and_the_silent_u(string word, string expected)
    {
        Say(word).ShouldBe(expected);
    }

    [Theory]
    [InlineData("rato", "xˈatʊ")]
    [InlineData("carro", "kˈaxʊ")]
    [InlineData("caro", "kˈaɾʊ")]
    [InlineData("praia", "prˈaɪæ")]
    [InlineData("honra", "ˈoŋxæ")]
    public void The_r_has_four_sounds(string word, string expected)
    {
        Say(word).ShouldBe(expected);
    }

    [Theory]
    [InlineData("casa", "kˈazæ")]
    [InlineData("passa", "pˈasæ")]
    [InlineData("sapo", "sˈapʊ")]
    [InlineData("nasce", "nˈasy")]
    public void The_s_between_vowels_is_a_z_and_double_or_before_ce_is_an_s(string word, string expected)
    {
        Say(word).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Natal", "natˈaʊ")]
    [InlineData("alto", "ˈaʊtʊ")]
    [InlineData("lado", "lˈadʊ")]
    public void The_l_before_a_consonant_or_at_the_end_sounds_like_a_u(string word, string expected)
    {
        Say(word).ShouldBe(expected);
    }

    [Theory]
    [InlineData("ponto", "pˈoŋtʊ")]
    [InlineData("campo", "kˈɐ̃mpʊ")]
    [InlineData("bom", "bˈoŋ")]
    [InlineData("irmã", "iɾəmˈɐ̃")]
    [InlineData("então", "eɪŋtˈɐ̃ʊ̃")]
    [InlineData("ações", "asˈõjs")]
    public void Nasal_vowels(string word, string expected)
    {
        Say(word).ShouldBe(expected.Normalize(System.Text.NormalizationForm.FormD));
    }

    [Theory]
    [InlineData("café", "kafˈɛ")]
    [InlineData("você", "vosˈe")]
    [InlineData("avô", "avˈo")]
    [InlineData("pé", "pˈɛ")]
    public void An_accent_says_which_vowel_is_stressed_and_how_open_it_is(string word, string expected)
    {
        Say(word).ShouldBe(expected);
    }

    [Theory]
    [InlineData("casa", "kˈazæ")] // ends in a: the one before the last
    [InlineData("Natal", "natˈaʊ")] // ends in l: the last
    [InlineData("amor", "æmˈor")]
    [InlineData("ouro", "ˈoʊɾʊ")]
    [InlineData("Maria", "mˌaɾˈiæ")]
    public void Without_an_accent_the_stress_follows_the_ending(string word, string expected)
    {
        Say(word).ShouldBe(expected);
    }

    [Theory]
    [InlineData("de", "dʒy")]
    [InlineData("o", "ʊ")]
    [InlineData("e", "i")]
    [InlineData("a", "a")]
    public void Little_words_have_no_stress_of_their_own(string word, string expected)
    {
        Say(word).ShouldBe(expected);
    }

    [Fact]
    public void Text_is_read_in_capitals_or_lowercase_alike()
    {
        PortuguesePhonemizer.Phonemize("PRONTO PARA O TÁXI").ShouldBe(PortuguesePhonemizer.Phonemize("pronto para o táxi"));
    }

    [Fact]
    public void Punctuation_that_shapes_the_intonation_is_kept_and_the_rest_is_dropped()
    {
        PortuguesePhonemizer.Phonemize("Repita, por favor! Ok? (sim) \"não\"").ShouldNotContain("(");
        PortuguesePhonemizer.Phonemize("sim, não.").ShouldBe("sˈiŋ, nˈɐ̃ʊ̃.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("...")]
    public void Nothing_to_read_gives_no_phonemes_or_only_punctuation(string text)
    {
        PortuguesePhonemizer.Phonemize(text).Where(char.IsLetter).ShouldBeEmpty();
    }

    [Fact]
    public void A_null_text_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => PortuguesePhonemizer.Phonemize(null!));
    }
}
