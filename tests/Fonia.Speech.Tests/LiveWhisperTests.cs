using Fonia.Core.Audio;
using Fonia.Core.Speech;

namespace Fonia.Speech.Tests;

/// <summary>
/// Runs the real recognizer on a recording. Skipped unless <c>FONIA_LIVE_WHISPER=1</c>: it needs the 466 MB model and a
/// WAV file (16 kHz, mono, 16 bits, such as the ones <c>fonia --mic-test</c> writes), so CI and other machines just
/// report it as skipped.
/// <code>
/// $env:FONIA_LIVE_WHISPER = "1"; $env:FONIA_LIVE_WHISPER_WAV = "fala.wav"; dotnet test --filter-class "*LiveWhisperTests"
/// </code>
/// The model comes from the default folder, or from FONIA_LIVE_WHISPER_MODEL.
/// </summary>
[Trait("Category", "Live")]
public class LiveWhisperTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FONIA_LIVE_WHISPER") == "1";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Needs the model and a WAV recording. Set FONIA_LIVE_WHISPER=1 and FONIA_LIVE_WHISPER_WAV to run.")]
    public async Task It_transcribes_a_real_recording()
    {
        var wav = Environment.GetEnvironmentVariable("FONIA_LIVE_WHISPER_WAV");
        Assert.SkipWhen(string.IsNullOrEmpty(wav) || !File.Exists(wav), "FONIA_LIVE_WHISPER_WAV must point to a WAV file.");

        var model = Environment.GetEnvironmentVariable("FONIA_LIVE_WHISPER_MODEL") ?? WhisperModelLocator.Resolve(null, null);
        var speech = WavDecoder.Decode(await File.ReadAllBytesAsync(wav!, TestContext.Current.CancellationToken));

        await using var transcriber = new WhisperTranscriber(new WhisperOptions(model));
        var transcript = await transcriber.TranscribeAsync(speech, new TranscriptionContext("PR-ABC"), TestContext.Current.CancellationToken);

        transcript.Text.ShouldNotBeNullOrWhiteSpace();
        transcript.Elapsed.ShouldBeGreaterThan(TimeSpan.Zero);
    }
}
