using Fonia.Core.Speech;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Fonia.Speech;

/// <summary>
/// Text to speech with Kokoro-82M on the CPU, fully offline. The text goes through the pronunciation layer and the
/// phonemizer of the Core, then the model turns the phonemes into a voice at 24 kHz. The model loads on the first call
/// and stays loaded; the silence the model leaves around the sentence is trimmed, because the pilot waits for it.
/// </summary>
public sealed class KokoroSynthesizer(KokoroOptions options) : ISynthesizer, IWarmUp
{
    /// <summary>The sample rate the model speaks at.</summary>
    public const int SampleRate = 24_000;

    private static readonly float[] Pause = new float[SampleRate / 6];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, float[]> _styles = new(StringComparer.Ordinal);
    private InferenceSession? _session;
    private bool _disposed;

    public async Task<SynthesizedSpeech> SynthesizeAsync(string text, Voice voice, CancellationToken ct, double pace = 1.0)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(voice);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        // Cheap checks first: nothing to say, or no model, should not cost a model load.
        var chunks = PhonemeChunker.Split(voice.Language == "en" ? EnglishPhonemizer.Phonemize(text) : Pronunciation.ToPhonemes(text), KokoroTokenizer.MaxPhonemes)
            .Select(KokoroTokenizer.Tokenize)
            .Where(tokens => tokens.Length > 0)
            .ToList();

        if (chunks.Count == 0)
        {
            throw new ArgumentException($"Não há nada para falar em '{text}'.", nameof(text));
        }

        if (!File.Exists(options.ModelPath) || !File.Exists(options.VoicesPath))
        {
            throw KokoroModelLocator.NotFound([.. new[] { options.ModelPath, options.VoicesPath }.Where(p => !File.Exists(p))]);
        }

        await _gate.WaitAsync(ct);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return await Task.Run(() => Speak(chunks, voice, SpeedFor(pace), ct), ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Loads the model and the first voice and says one word, so that the first real call pays for nothing.</summary>
    public async Task WarmUpAsync(TranscriptionContext context, CancellationToken ct) =>
        _ = await SynthesizeAsync("Teste.", VoiceCatalog.Default[0], ct);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync();

        try
        {
            _disposed = true;
            _session?.Dispose();
            _session = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private SynthesizedSpeech Speak(List<long[]> chunks, Voice voice, float speed, CancellationToken ct)
    {
        var session = _session ??= new InferenceSession(
            options.ModelPath,
            new SessionOptions
            {
                IntraOpNumThreads = options.EffectiveThreads,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            });

        if (!_styles.TryGetValue(voice.Id, out var styles))
        {
            styles = _styles[voice.Id] = NpzVoiceReader.ReadStyles(options.VoicesPath, voice.Id);
        }

        var audio = new List<float>();

        foreach (var tokens in chunks)
        {
            ct.ThrowIfCancellationRequested();

            var sentence = SilenceTrimmer.Trim(Run(session, tokens, NpzVoiceReader.StyleFor(styles, tokens.Length), speed), SampleRate);

            if (audio.Count > 0)
            {
                audio.AddRange(Pause);
            }

            audio.AddRange(sentence);
        }

        return new SynthesizedSpeech([.. audio], SampleRate);
    }

    // The speed of the options times the pace asked for, inside what the model accepts (0.5 to 2.0).
    private float SpeedFor(double pace) => (float)Math.Clamp(options.Speed * pace, 0.5, 2.0);

    private static float[] Run(InferenceSession session, long[] tokens, ReadOnlySpan<float> style, float speed)
    {
        var padded = KokoroTokenizer.Pad(tokens);

        using var results = session.Run(
        [
            NamedOnnxValue.CreateFromTensor("tokens", new DenseTensor<long>(padded, [1, padded.Length])),
            NamedOnnxValue.CreateFromTensor("style", new DenseTensor<float>(style.ToArray(), [1, NpzVoiceReader.StyleSize])),
            NamedOnnxValue.CreateFromTensor("speed", new DenseTensor<float>(new[] { speed }, [1])),
        ]);

        return results[0].AsTensor<float>().ToArray();
    }
}
