namespace Fonia.Speech.Tests;

public class KokoroTokenizerTests
{
    [Fact]
    public void Each_phoneme_becomes_the_number_the_model_gives_it()
    {
        // ids from the model's own vocabulary: space 16, "a" 43, "b" 44, stress mark 156, "ʊ" 135.
        KokoroTokenizer.Tokenize("ab ˈʊ").ShouldBe([43, 44, 16, 156, 135]);
    }

    [Fact]
    public void Punctuation_the_model_knows_is_kept()
    {
        KokoroTokenizer.Tokenize("a, b.").ShouldBe([43, 3, 16, 44, 4]);
    }

    [Fact]
    public void A_symbol_the_model_does_not_know_is_dropped_not_guessed()
    {
        KokoroTokenizer.Tokenize("a@#b1").ShouldBe([43, 44]);
    }

    [Fact]
    public void The_combining_tilde_is_a_phoneme_of_its_own()
    {
        KokoroTokenizer.Tokenize("ɐ̃").ShouldBe([70, 17]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    public void Nothing_the_model_knows_gives_no_tokens(string phonemes)
    {
        KokoroTokenizer.Tokenize(phonemes).ShouldBeEmpty();
    }

    [Fact]
    public void The_model_pads_the_sequence_with_a_zero_on_each_side()
    {
        KokoroTokenizer.Pad([43, 44]).ShouldBe([0, 43, 44, 0]);
    }

    [Fact]
    public void The_longest_sequence_is_510_phonemes()
    {
        KokoroTokenizer.MaxPhonemes.ShouldBe(510);
    }

    [Fact]
    public void Every_symbol_the_phonemizer_writes_is_in_the_vocabulary_of_the_model()
    {
        // The same words the Core tests check against the alphabet; here against the numbers the model really has.
        var phonemes = Fonia.Core.Speech.Pronunciation.ToPhonemes(
            "PAPA ROMEO ALFA BRAVO CHARLIE, autorizado pushback e acionamento, chame pronto para o táxi na PISTA UNO CINCO. SOLO Curitiba, informações.");

        KokoroTokenizer.Tokenize(phonemes).Length.ShouldBe(phonemes.Length, "no phoneme may be dropped");
    }
}
