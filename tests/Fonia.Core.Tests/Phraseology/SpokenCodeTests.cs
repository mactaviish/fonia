using Fonia.Core.Phraseology;

namespace Fonia.Core.Tests.Phraseology;

public class SpokenCodeTests
{
    [Theory]
    [InlineData("Três, um-zero!", "tres um zero")]
    [InlineData("  PISTA  Zero Nove ", "pista zero nove")]
    [InlineData("118,1", "118 decimal 1")]
    [InlineData("118.1", "118 decimal 1")]
    [InlineData("x-ray", "x ray")]
    [InlineData("São Paulo", "sao paulo")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Tokens_have_no_accents_punctuation_or_case(string? text, string expected)
    {
        string.Join(' ', SpokenTokens.Tokenize(text)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("um um oito decimal um", "118.1")]
    [InlineData("uno uno oito decimal uno", "118.1")]
    [InlineData("zero nove", "09")]
    [InlineData("meia", "6")]
    [InlineData("seis", "6")]
    [InlineData("dois três esquerda", "23")]
    [InlineData("cinco mil", "5000")]
    [InlineData("mil", "1000")]
    [InlineData("um mil", "1000")]
    [InlineData("um zero um sete", "1017")]
    [InlineData("1017", "1017")]
    [InlineData("quatro três dois uno", "4321")]
    public void Digits_are_read_one_by_one_or_as_written(string spoken, string expected)
    {
        SpokenCode.Decode(spoken).ShouldBe(expected);
    }

    [Theory]
    [InlineData("quinze", "15")]
    [InlineData("dez", "10")]
    [InlineData("vinte e cinco", "25")]
    [InlineData("vinte cinco", "25")]
    [InlineData("trinta e seis", "36")]
    [InlineData("trinta", "30")]
    [InlineData("dezesseis", "16")]
    public void Numbers_said_as_words_are_understood_too(string spoken, string expected)
    {
        SpokenCode.Decode(spoken).ShouldBe(expected);
    }

    [Fact]
    public void A_tens_word_followed_by_a_conjunction_does_not_swallow_it()
    {
        var tokens = SpokenTokens.Tokenize("vinte e pista");
        var index = 0;

        SpokenCode.TryRead(tokens, ref index, out var value).ShouldBeTrue();

        value.ShouldBe("20");
        index.ShouldBe(1);
    }

    [Theory]
    [InlineData("papa romeo alfa alfa papa", "PRAAP")]
    [InlineData("Papa Romeu Alfa Bravo Charlie", "PRABC")]
    [InlineData("p r a b c", "PRABC")]
    [InlineData("golf oscar whiskey", "GOW")]
    [InlineData("x-ray yankee zulu", "XYZ")]
    [InlineData("xis", "X")]
    [InlineData("juliett kilo lima mike november", "JKLMN")]
    [InlineData("sierra tango uniform victor", "STUV")]
    [InlineData("quebec delta echo foxtrot hotel india", "QDEFHI")]
    public void Phonetic_words_and_plain_letters_become_letters(string spoken, string expected)
    {
        SpokenCode.Decode(spoken).ShouldBe(expected);
    }

    [Fact]
    public void A_run_stops_at_the_first_word_that_is_not_a_number_or_letter()
    {
        var tokens = SpokenTokens.Tokenize("pista zero nove esquerda autorizado");

        SpokenCode.ReadRun(tokens, 0, out var end).ShouldBe(string.Empty);
        end.ShouldBe(0);

        SpokenCode.ReadRun(tokens, 1, out end).ShouldBe("09");
        end.ShouldBe(3);
    }

    [Fact]
    public void A_registration_the_speech_to_text_wrote_as_one_piece_is_kept()
    {
        SpokenCode.Decode("PT123").ShouldBe("PT123");
    }

    [Fact]
    public void Words_that_are_not_numbers_or_letters_are_ignored_by_decode()
    {
        SpokenCode.Decode("solo, pista zero nove, táxi").ShouldBe("09");
    }
}
