using Fonia.Core.Speech;

namespace Fonia.Core.Tests.Speech;

public class PronunciationTests
{
    [Theory]
    [InlineData("ROMEO", "romeu")]
    [InlineData("Romeo", "romeu")]
    [InlineData("charlie", "tchárli")]
    [InlineData("PUSHBACK", "púchibéqui")]
    [InlineData("pushback", "púchibéqui")]
    [InlineData("X-RAY", "écs rei")]
    [InlineData("WHISKEY", "uísque")]
    [InlineData("UNIFORM", "iuniform")]
    [InlineData("foxtrot", "fóxtroti")]
    public void Foreign_words_get_a_brazilian_spelling_whatever_the_case(string written, string spoken)
    {
        Pronunciation.Respell(written).ShouldBe(spoken);
    }

    [Fact]
    public void Punctuation_and_the_other_words_stay_where_they_are()
    {
        Pronunciation.Respell("PAPA ROMEO, ALFA: autorizado pushback e acionamento.")
            .ShouldBe("PAPA romeu, ALFA: autorizado púchibéqui e acionamento.");
    }

    [Fact]
    public void A_text_with_nothing_to_respell_is_returned_as_it_is()
    {
        const string text = "Repita.";

        Pronunciation.Respell(text).ShouldBe(text);
    }

    [Fact]
    public void Every_word_of_the_phonetic_alphabet_is_either_respelled_or_read_right_by_the_rules()
    {
        // The twenty-six words of SpokenText: none may reach the phonemizer as an English word it would read as Portuguese.
        string[] needRespelling = ["delta", "echo", "foxtrot", "golf", "hotel", "juliett", "mike", "quebec", "romeo", "sierra", "uniform", "victor", "whiskey", "x-ray", "yankee", "charlie"];

        foreach (var word in needRespelling)
        {
            Pronunciation.Respell(word).ShouldNotBe(word, word);
        }
    }

    [Fact]
    public void A_sentence_of_the_controller_becomes_phonemes_with_the_pause_of_its_commas()
    {
        var phonemes = Pronunciation.ToPhonemes("PAPA ROMEO ALFA BRAVO CHARLIE, prossiga.");

        phonemes.ShouldBe("pˈapæ xomˈeʊ ˈaʊfæ brˈavʊ tʃˈaɾəli, prˌosˈiɡæ.");
    }

    // Heard in the live test: "negativo, autorizado" had almost no gap between the two. A comma makes the voice model pause
    // about 230 ms after "negativo"; three dots make it pause about 340 ms, which the maintainer chose by ear.
    [Theory]
    [InlineData("PAPA ROMEO, negativo, autorizado táxi.", "PAPA romeu, negativo... autorizado táxi.")]
    [InlineData("negativo, autorizado", "negativo... autorizado")]
    [InlineData("NEGATIVO, autorizado", "NEGATIVO... autorizado")]
    public void The_comma_after_negativo_becomes_a_longer_pause_for_the_voice_only(string written, string spoken)
    {
        Pronunciation.Respell(written).ShouldBe(spoken);
    }

    [Theory]
    [InlineData("negativo autorizado")]
    [InlineData("negativo.")]
    [InlineData("autorizado, negativo")]
    [InlineData("negativos, autorizado")]
    public void Only_a_comma_right_after_the_word_negativo_is_changed(string text)
    {
        Pronunciation.Respell(text).ShouldBe(text);
    }

    [Fact]
    public void The_pause_reaches_the_phonemes_as_three_stops_before_the_next_word()
    {
        var phonemes = Pronunciation.ToPhonemes("PAPA ROMEO ALFA BRAVO CHARLIE, negativo, autorizado táxi.");

        phonemes.ShouldContain("nˌeɡatʃˈivʊ... ˌaʊtoɾizˈadʊ");
    }
}
