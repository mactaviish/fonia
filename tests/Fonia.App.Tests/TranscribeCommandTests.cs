using System.Text;
using Fonia.Core.Audio;
using Fonia.Core.Nlu;
using Fonia.Core.Speech;

namespace Fonia.App.Tests;

public sealed class TranscribeCommandTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fonia-transcribe-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _output = new(new StringBuilder()) { NewLine = "\n" };
    private readonly StringWriter _error = new(new StringBuilder()) { NewLine = "\n" };

    public TranscribeCommandTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeTranscriber(Func<RecordedSpeech, TranscriptionContext, Transcript> answer) : ITranscriber
    {
        public List<TranscriptionContext> Contexts { get; } = [];

        public Task<Transcript> TranscribeAsync(RecordedSpeech speech, TranscriptionContext context, CancellationToken ct)
        {
            Contexts.Add(context);
            return Task.FromResult(answer(speech, context));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private string WriteWav(int milliseconds = 1500, AudioFormat? format = null)
    {
        var path = Path.Combine(_directory, "fala.wav");
        var f = format ?? AudioFormat.Speech;
        File.WriteAllBytes(path, WavEncoder.Encode(new RecordedSpeech(f, new byte[f.BytesFor(TimeSpan.FromMilliseconds(milliseconds))])));
        return path;
    }

    private TranscribeCommand Create(ITranscriber transcriber) => new(transcriber, new RuleBasedNlu(), _output, _error);

    [Fact]
    public async Task It_shows_the_text_the_times_and_what_the_nlu_understood()
    {
        var transcriber = new FakeTranscriber((speech, _) => new Transcript("Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento", speech.Duration, TimeSpan.FromMilliseconds(1900)));

        var code = await Create(transcriber).RunAsync(WriteWav(), "PR-ABC", Ct);

        code.ShouldBe(0);
        var text = _output.ToString();
        text.ShouldContain("Fala: 1,5 s");
        text.ShouldContain("reconhecimento: 1,9 s");
        text.ShouldContain("Texto: Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento");
        text.ShouldContain("Entendido: RequestStartup");
        text.ShouldContain("PRABC");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task It_gives_the_recognizer_the_callsign_to_expect()
    {
        var transcriber = new FakeTranscriber((speech, _) => new Transcript("x", speech.Duration, TimeSpan.Zero));

        await Create(transcriber).RunAsync(WriteWav(), "PR-ABC", Ct);

        transcriber.Contexts.ShouldHaveSingleItem().Callsign.ShouldBe("PR-ABC");
    }

    [Fact]
    public async Task Without_a_callsign_it_still_runs()
    {
        var transcriber = new FakeTranscriber((speech, _) => new Transcript("solicito acionamento", speech.Duration, TimeSpan.Zero));

        var code = await Create(transcriber).RunAsync(WriteWav(), null, Ct);

        code.ShouldBe(0);
        transcriber.Contexts.ShouldHaveSingleItem().Callsign.ShouldBeNull();
        _output.ToString().ShouldContain("Entendido:");
    }

    [Fact]
    public async Task Silence_is_reported_as_nothing_heard()
    {
        var transcriber = new FakeTranscriber((speech, _) => new Transcript(string.Empty, speech.Duration, TimeSpan.Zero));

        var code = await Create(transcriber).RunAsync(WriteWav(), null, Ct);

        code.ShouldBe(0);
        _output.ToString().ShouldContain("nada reconhecido");
    }

    [Fact]
    public async Task A_missing_file_is_an_error_not_a_crash()
    {
        var transcriber = new FakeTranscriber((s, _) => new Transcript("x", s.Duration, TimeSpan.Zero));

        var code = await Create(transcriber).RunAsync(Path.Combine(_directory, "nao-existe.wav"), null, Ct);

        code.ShouldBe(1);
        _error.ToString().ShouldContain("Não foi possível ler");
    }

    [Fact]
    public async Task A_file_that_is_not_a_wav_is_an_error_with_the_reason()
    {
        var path = Path.Combine(_directory, "texto.wav");
        await File.WriteAllTextAsync(path, "isto não é áudio, é só texto escrito num arquivo qualquer", Ct);
        var transcriber = new FakeTranscriber((s, _) => new Transcript("x", s.Duration, TimeSpan.Zero));

        var code = await Create(transcriber).RunAsync(path, null, Ct);

        code.ShouldBe(1);
        _error.ToString().ShouldContain("WAV");
    }

    [Fact]
    public async Task Audio_in_another_format_says_how_to_convert_it()
    {
        var transcriber = new FakeTranscriber((_, _) => throw new InvalidOperationException("must not be called"));

        var code = await Create(transcriber).RunAsync(WriteWav(format: new AudioFormat(44_100, 2, 16)), null, Ct);

        code.ShouldBe(1);
        _error.ToString().ShouldContain("16 kHz");
        _error.ToString().ShouldContain("mono");
        transcriber.Contexts.ShouldBeEmpty("a wrong format must not reach the recognizer");
    }

    [Fact]
    public async Task A_missing_model_prints_its_message_and_fails()
    {
        var transcriber = new FakeTranscriber((_, _) => throw new FileNotFoundException("Modelo de voz não encontrado: x.bin"));

        var code = await Create(transcriber).RunAsync(WriteWav(), null, Ct);

        code.ShouldBe(1);
        _error.ToString().ShouldContain("Modelo de voz não encontrado");
    }
}
