using Fonia.Core.Speech;

namespace Fonia.Core.Tests.Speech;

public class WhisperPromptBuilderTests
{
    [Fact]
    public void Without_context_it_is_the_aviation_vocabulary_alone()
    {
        var prompt = WhisperPromptBuilder.Build(TranscriptionContext.Empty);

        prompt.ShouldStartWith("Comunicação de rádio aeronáutica em português.");
        foreach (var word in new[] { "Solo", "Torre", "pushback", "acionamento", "táxi", "pista", "ponto de espera", "QNH" })
        {
            prompt.ShouldContain(word);
        }
    }

    [Fact]
    public void The_callsign_goes_in_as_it_is_spoken_because_that_is_what_the_recognizer_has_to_write()
    {
        var prompt = WhisperPromptBuilder.Build(new TranscriptionContext(Callsign: "PR-ABC"));

        prompt.ShouldContain("Papa Romeo Alfa Bravo Charlie");
        prompt.ShouldNotContain("PRABC", Case.Sensitive);
        prompt.ShouldNotContain("PAPA ROMEO", Case.Sensitive);
    }

    [Fact]
    public void A_company_callsign_uses_the_telephony()
    {
        var prompt = WhisperPromptBuilder.Build(new TranscriptionContext(Callsign: "GLO1234"));

        prompt.ShouldContain("Gol");
        prompt.ShouldContain("Uno Dois Três Quatro");
    }

    [Fact]
    public void The_station_is_included_as_given()
    {
        var prompt = WhisperPromptBuilder.Build(new TranscriptionContext(Station: "Solo Curitiba"));

        prompt.ShouldContain("Solo Curitiba");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--")]
    public void A_missing_or_unspellable_callsign_is_left_out_instead_of_failing(string? callsign)
    {
        var prompt = WhisperPromptBuilder.Build(new TranscriptionContext(Callsign: callsign));

        prompt.ShouldBe(WhisperPromptBuilder.Build(TranscriptionContext.Empty));
    }

    [Fact]
    public void It_has_no_double_spaces_and_stays_short_enough_for_the_recognizer()
    {
        var prompt = WhisperPromptBuilder.Build(new TranscriptionContext("PR-ABC", "Torre Foz do Iguaçu"));

        prompt.ShouldNotContain("  ");
        prompt.Length.ShouldBeLessThan(400, "the recognizer reads only the last ~224 tokens of a prompt");
    }
}
