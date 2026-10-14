using Fonia.Core.Speech;

namespace Fonia.Speech.Tests;

public sealed class KokoroSynthesizerTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fonia-kokoro-" + Guid.NewGuid().ToString("N"));

    public KokoroSynthesizerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static readonly Voice Dora = VoiceCatalog.Default[0];

    [Fact]
    public void The_default_files_are_the_ones_of_the_kokoro_release_in_the_same_folder_as_the_recognizer()
    {
        KokoroModelLocator.ModelFileName.ShouldBe("kokoro-v1.0.onnx");
        KokoroModelLocator.VoicesFileName.ShouldBe("voices-v1.0.bin");
        KokoroModelLocator.DefaultDirectory.ShouldBe(WhisperModelLocator.DefaultDirectory);
    }

    [Fact]
    public void It_finds_the_model_and_the_voices_in_the_folder_given()
    {
        FakeModelFiles.Create(Path.Combine(_directory, "kokoro-v1.0.onnx"));
        FakeModelFiles.Create(Path.Combine(_directory, "voices-v1.0.bin"));

        var (model, voices) = KokoroModelLocator.Resolve(_directory);

        model.ShouldBe(Path.Combine(_directory, "kokoro-v1.0.onnx"));
        voices.ShouldBe(Path.Combine(_directory, "voices-v1.0.bin"));
    }

    [Fact]
    public void Missing_files_are_listed_with_where_to_get_them()
    {
        var error = Should.Throw<FileNotFoundException>(() => KokoroModelLocator.Resolve(_directory));

        error.Message.ShouldContain(Path.Combine(_directory, "kokoro-v1.0.onnx"));
        error.Message.ShouldContain(Path.Combine(_directory, "voices-v1.0.bin"));
        error.Message.ShouldContain("github.com/thewh1teagle/kokoro-onnx/releases");
        error.Message.ShouldContain("--tts-dir");
    }

    [Fact]
    public void Only_the_missing_file_is_named()
    {
        FakeModelFiles.Create(Path.Combine(_directory, "voices-v1.0.bin"));

        var error = Should.Throw<FileNotFoundException>(() => KokoroModelLocator.Resolve(_directory));

        error.Message.ShouldContain("kokoro-v1.0.onnx");
        error.Message.ShouldNotContain(Path.Combine(_directory, "voices-v1.0.bin"));
    }

    [Fact]
    public async Task Text_without_anything_to_say_is_refused_before_the_model_is_touched()
    {
        await using var synthesizer = new KokoroSynthesizer(new KokoroOptions("no-model.onnx", "no-voices.bin"));

        await Should.ThrowAsync<ArgumentException>(() => synthesizer.SynthesizeAsync("123 456", Dora, Ct));
        await Should.ThrowAsync<ArgumentException>(() => synthesizer.SynthesizeAsync("   ", Dora, Ct));
    }

    [Fact]
    public async Task A_missing_model_is_a_clear_error_that_says_where_to_get_it()
    {
        await using var synthesizer = new KokoroSynthesizer(new KokoroOptions(Path.Combine(_directory, "m.onnx"), Path.Combine(_directory, "v.bin")));

        var error = await Should.ThrowAsync<FileNotFoundException>(() => synthesizer.SynthesizeAsync("Repita.", Dora, Ct));

        error.Message.ShouldContain("m.onnx");
        error.Message.ShouldContain("github.com");
    }

    [Fact]
    public async Task A_cancelled_call_does_not_start()
    {
        await using var synthesizer = new KokoroSynthesizer(new KokoroOptions("m.onnx", "v.bin"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => synthesizer.SynthesizeAsync("Repita.", Dora, cancellation.Token));
    }

    [Fact]
    public async Task After_disposal_it_refuses_new_work()
    {
        var synthesizer = new KokoroSynthesizer(new KokoroOptions("m.onnx", "v.bin"));
        await synthesizer.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() => synthesizer.SynthesizeAsync("Repita.", Dora, Ct));
    }

    [Fact]
    public void Speed_must_be_within_what_the_model_accepts()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new KokoroOptions("m", "v", Speed: 0.4));
        Should.Throw<ArgumentOutOfRangeException>(() => new KokoroOptions("m", "v", Speed: 2.1));
        new KokoroOptions("m", "v", Speed: 1.1).Speed.ShouldBe(1.1);
    }

    [Fact]
    public void Threads_default_to_half_of_the_cores_because_the_simulator_needs_the_rest()
    {
        new KokoroOptions("m", "v").EffectiveThreads.ShouldBe(Math.Clamp(Environment.ProcessorCount / 2, 1, 8));
        new KokoroOptions("m", "v", Threads: 2).EffectiveThreads.ShouldBe(2);
    }

    [Fact]
    public async Task Warming_up_without_the_model_fails_the_way_the_first_real_call_would()
    {
        await using var synthesizer = new KokoroSynthesizer(new KokoroOptions(Path.Combine(_directory, "m.onnx"), Path.Combine(_directory, "v.bin")));

        var error = await Should.ThrowAsync<FileNotFoundException>(() => synthesizer.WarmUpAsync(TranscriptionContext.Empty, Ct));

        error.Message.ShouldContain("m.onnx");
    }

    [Fact]
    public void The_model_and_the_voices_are_looked_for_together_in_each_folder_in_order()
    {
        var bundled = Path.Combine(_directory, "bundled");
        var downloaded = Path.Combine(_directory, "downloaded");
        FakeModelFiles.Create(Path.Combine(bundled, "kokoro-v1.0.onnx")); // the voices are missing here
        FakeModelFiles.Create(Path.Combine(downloaded, "kokoro-v1.0.onnx"));
        FakeModelFiles.Create(Path.Combine(downloaded, "voices-v1.0.bin"));

        var (model, voices) = KokoroModelLocator.Resolve(null, [bundled, downloaded]);

        model.ShouldBe(Path.Combine(downloaded, "kokoro-v1.0.onnx"));
        voices.ShouldBe(Path.Combine(downloaded, "voices-v1.0.bin"));
    }

    [Fact]
    public void The_message_offers_the_download_command()
    {
        Should.Throw<FileNotFoundException>(() => KokoroModelLocator.Resolve(_directory)).Message.ShouldContain("--download-models");
    }
}
