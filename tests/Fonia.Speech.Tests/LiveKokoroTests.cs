using Fonia.Core.Speech;

namespace Fonia.Speech.Tests;

/// <summary>
/// Runs the real voice model. Skipped unless <c>FONIA_LIVE_KOKORO=1</c>: it needs the model (325 MB) and the voices
/// file, so CI and other machines just report it as skipped.
/// <code>
/// $env:FONIA_LIVE_KOKORO = "1"; dotnet test --filter-class "*LiveKokoroTests"
/// </code>
/// The files come from the default folder, or from the folder in FONIA_LIVE_KOKORO_DIR.
/// </summary>
[Trait("Category", "Live")]
public class LiveKokoroTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FONIA_LIVE_KOKORO") == "1";

    [Theory(SkipUnless = nameof(Enabled), Skip = "Needs the Kokoro model and voices. Set FONIA_LIVE_KOKORO=1 to run.")]
    [InlineData("pf_dora")]
    [InlineData("pm_alex")]
    public async Task It_speaks_a_sentence_of_the_controller_with_each_approved_voice(string voiceId)
    {
        var (model, voices) = KokoroModelLocator.Resolve(Environment.GetEnvironmentVariable("FONIA_LIVE_KOKORO_DIR"));
        await using var synthesizer = new KokoroSynthesizer(new KokoroOptions(model, voices));

        var speech = await synthesizer.SynthesizeAsync(
            "PAPA ROMEO ALFA BRAVO CHARLIE, autorizado táxi para o ponto de espera da PISTA UNO CINCO.",
            VoiceCatalog.Find(voiceId)!,
            TestContext.Current.CancellationToken);

        speech.SampleRate.ShouldBe(24_000);
        speech.Duration.ShouldBeInRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(12));
        speech.Samples.ShouldAllBe(s => float.IsFinite(s));
        speech.Samples.Max(Math.Abs).ShouldBeGreaterThan(0.1f);
        Math.Abs(speech.Samples[0]).ShouldBeLessThan(0.05f, "the silence the model leaves at the start is trimmed");
    }
}
