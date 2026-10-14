using Fonia.Core.Audio;
using Fonia.Core.Speech;

namespace Fonia.Speech.Tests;

public class WhisperTranscriberTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Obrigado.", "Obrigado.")]
    [InlineData("  Solo Curitiba  ", "Solo Curitiba")]
    [InlineData("[BLANK_AUDIO]", "")]
    [InlineData(" [Música] ", "")]
    [InlineData("(música)", "")]
    [InlineData("[Silêncio]", "")]
    [InlineData("Pista [15]", "Pista [15]")]
    [InlineData("", "")]
    public void Annotations_the_recognizer_writes_for_non_speech_are_dropped(string segment, string expected)
    {
        WhisperTranscriber.CleanSegment(segment).ShouldBe(expected);
    }

    [Fact]
    public async Task Audio_in_the_wrong_format_is_refused_before_the_model_is_touched()
    {
        await using var transcriber = new WhisperTranscriber(new WhisperOptions(Path.Combine(Path.GetTempPath(), "does-not-exist.bin")));
        var speech = new RecordedSpeech(new AudioFormat(44_100, 2, 16), new byte[64]);

        var error = await Should.ThrowAsync<ArgumentException>(() => transcriber.TranscribeAsync(speech, TranscriptionContext.Empty, Ct));

        error.Message.ShouldContain("16 kHz");
    }

    [Fact]
    public async Task A_missing_model_is_a_clear_error_that_says_where_to_get_it()
    {
        var path = Path.Combine(Path.GetTempPath(), "fonia-no-model", "ggml-small.bin");
        await using var transcriber = new WhisperTranscriber(new WhisperOptions(path));
        var speech = new RecordedSpeech(AudioFormat.Speech, new byte[64]);

        var error = await Should.ThrowAsync<FileNotFoundException>(() => transcriber.TranscribeAsync(speech, TranscriptionContext.Empty, Ct));

        error.Message.ShouldContain(path);
        error.Message.ShouldContain("huggingface.co");
    }

    [Fact]
    public async Task A_cancelled_call_does_not_start()
    {
        await using var transcriber = new WhisperTranscriber(new WhisperOptions("x.bin"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            transcriber.TranscribeAsync(new RecordedSpeech(AudioFormat.Speech, new byte[64]), TranscriptionContext.Empty, cancellation.Token));
    }

    [Fact]
    public async Task After_disposal_it_refuses_new_work()
    {
        var transcriber = new WhisperTranscriber(new WhisperOptions("x.bin"));
        await transcriber.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() =>
            transcriber.TranscribeAsync(new RecordedSpeech(AudioFormat.Speech, new byte[64]), TranscriptionContext.Empty, Ct));
    }

    [Fact]
    public void Threads_default_to_half_of_the_cores_because_the_simulator_needs_the_rest()
    {
        var options = new WhisperOptions("x.bin");

        options.EffectiveThreads.ShouldBe(Math.Clamp(Environment.ProcessorCount / 2, 1, 8));
        new WhisperOptions("x.bin", Threads: 3).EffectiveThreads.ShouldBe(3);
    }
}
