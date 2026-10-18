using System.Text;
using Fonia.Core.Audio;
using Fonia.Core.Speech;

namespace Fonia.App.Tests;

public sealed class SpeakCommandTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fonia-speak-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _output = new(new StringBuilder()) { NewLine = "\n" };
    private readonly StringWriter _error = new(new StringBuilder()) { NewLine = "\n" };

    public SpeakCommandTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeSynthesizer(Func<string, Voice, SynthesizedSpeech> answer) : ISynthesizer
    {
        public List<(string Text, Voice Voice)> Calls { get; } = [];

        public Task<SynthesizedSpeech> SynthesizeAsync(string text, Voice voice, CancellationToken ct, double pace = 1.0)
        {
            Calls.Add((text, voice));
            return Task.FromResult(answer(text, voice));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static SynthesizedSpeech OneSecondTone()
    {
        var samples = Enumerable.Range(0, 24_000).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / 24_000))).ToArray();
        return new SynthesizedSpeech(samples, 24_000);
    }

    private string Path1 => Path.Combine(_directory, "fala.wav");

    private SpeakCommand Create(ISynthesizer synthesizer, int seed = 1) => new(synthesizer, _output, _error, new Random(seed));

    [Fact]
    public async Task It_speaks_with_the_voice_asked_for_and_saves_a_wav()
    {
        var synthesizer = new FakeSynthesizer((_, _) => OneSecondTone());

        var code = await Create(synthesizer).RunAsync("Repita.", Path1, "pm_alex", RadioIntensity.Off, Ct);

        code.ShouldBe(0);
        synthesizer.Calls.ShouldHaveSingleItem().ShouldBe(("Repita.", VoiceCatalog.Find("pm_alex")!));
        var wav = WavDecoder.Decode(await File.ReadAllBytesAsync(Path1, Ct));
        wav.Format.SampleRate.ShouldBe(24_000);
        wav.Duration.ShouldBe(TimeSpan.FromSeconds(1));
        _output.ToString().ShouldContain("Alex (pm_alex)");
        _output.ToString().ShouldContain("efeito de rádio: desligado");
        _output.ToString().ShouldContain(Path1);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_voice_it_picks_one_of_the_approved_voices_at_random()
    {
        var used = new HashSet<string>();

        for (var seed = 0; seed < 30; seed++)
        {
            var synthesizer = new FakeSynthesizer((_, _) => OneSecondTone());
            await Create(synthesizer, seed).RunAsync("Repita.", Path1, null, RadioIntensity.Off, Ct);
            used.Add(synthesizer.Calls.Single().Voice.Id);
        }

        used.ShouldBe(["pf_dora", "pm_alex"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_radio_effect_makes_the_file_longer_with_the_click_and_the_squelch()
    {
        var synthesizer = new FakeSynthesizer((_, _) => OneSecondTone());

        await Create(synthesizer).RunAsync("Repita.", Path1, "pf_dora", RadioIntensity.Medium, Ct);

        WavDecoder.Decode(await File.ReadAllBytesAsync(Path1, Ct)).Duration.ShouldBeGreaterThan(TimeSpan.FromSeconds(1.15));
        _output.ToString().ShouldContain("efeito de rádio: média");
    }

    [Fact]
    public async Task A_voice_that_does_not_exist_is_an_error_that_lists_the_ones_that_do()
    {
        var synthesizer = new FakeSynthesizer((_, _) => OneSecondTone());

        var code = await Create(synthesizer).RunAsync("Repita.", Path1, "pm_santa", RadioIntensity.Off, Ct);

        code.ShouldBe(2);
        _error.ToString().ShouldContain("pm_santa");
        _error.ToString().ShouldContain("pf_dora");
        synthesizer.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_model_prints_its_message_and_fails()
    {
        var synthesizer = new FakeSynthesizer((_, _) => throw new FileNotFoundException("Modelo de voz do controlador não encontrado: x"));

        var code = await Create(synthesizer).RunAsync("Repita.", Path1, null, RadioIntensity.Off, Ct);

        code.ShouldBe(1);
        _error.ToString().ShouldContain("Modelo de voz do controlador não encontrado");
        File.Exists(Path1).ShouldBeFalse();
    }

    [Fact]
    public async Task Text_with_nothing_to_say_is_an_error_not_a_crash()
    {
        var synthesizer = new FakeSynthesizer((_, _) => throw new ArgumentException("Não há nada para falar em '123'."));

        var code = await Create(synthesizer).RunAsync("123", Path1, null, RadioIntensity.Off, Ct);

        code.ShouldBe(1);
        _error.ToString().ShouldContain("Não há nada para falar");
    }

    [Fact]
    public async Task A_file_that_cannot_be_written_is_an_error_not_a_crash()
    {
        var blocker = Path.Combine(_directory, "arquivo");
        await File.WriteAllTextAsync(blocker, "x", Ct);
        var synthesizer = new FakeSynthesizer((_, _) => OneSecondTone());

        var code = await Create(synthesizer).RunAsync("Repita.", Path.Combine(blocker, "fala.wav"), "pf_dora", RadioIntensity.Off, Ct);

        code.ShouldBe(1);
        _error.ToString().ShouldContain("Não foi possível gravar");
    }
}
