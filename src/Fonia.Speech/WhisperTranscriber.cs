using System.Diagnostics;
using System.Text;
using Fonia.Core.Audio;
using Fonia.Core.Speech;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Fonia.Speech;

/// <summary>
/// Speech to text with Whisper (whisper.cpp through Whisper.net), fully offline. The model loads on the first call and
/// stays loaded; the processor is rebuilt only when the prompt changes (a new callsign or station), so the usual call
/// pays for recognition alone.
/// </summary>
public sealed class WhisperTranscriber(WhisperOptions options) : ITranscriber, IWarmUp
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string? _processorPrompt;
    private bool _disposed;

    public async Task<Transcript> TranscribeAsync(RecordedSpeech speech, TranscriptionContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(speech);
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        // Cheap checks first: a wrong format or a missing model should not cost a model load.
        var samples = PcmConverter.ToFloat(speech);

        if (!File.Exists(options.ModelPath))
        {
            throw WhisperModelLocator.NotFound(options.ModelPath);
        }

        var prompt = WhisperPromptBuilder.Build(context, options.Telephony);

        await _gate.WaitAsync(ct);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var watch = Stopwatch.StartNew();
            var processor = await Task.Run(() => GetProcessor(prompt), ct);
            var text = new StringBuilder();

            await foreach (var segment in processor.ProcessAsync(samples, ct))
            {
                var clean = CleanSegment(segment.Text);

                if (clean.Length > 0)
                {
                    text.Append(text.Length > 0 ? " " : string.Empty).Append(clean);
                }
            }

            return new Transcript(text.ToString(), speech.Duration, watch.Elapsed);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Loads the model and recognizes a second of silence, with the prompt of the flight, so that the first real call pays for nothing.</summary>
    public async Task WarmUpAsync(TranscriptionContext context, CancellationToken ct) =>
        _ = await TranscribeAsync(new RecordedSpeech(AudioFormat.Speech, new byte[AudioFormat.Speech.BytesPerSecond]), context, ct);

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

            if (_processor is not null)
            {
                await _processor.DisposeAsync();
            }

            _factory?.Dispose();
            _processor = null;
            _factory = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The recognizer writes "[BLANK_AUDIO]" or "[Música]" for what is not speech; that is not something the pilot said.
    /// </summary>
    internal static string CleanSegment(string text)
    {
        var trimmed = text.Trim();
        var annotation = trimmed.Length >= 2
            && ((trimmed[0] == '[' && trimmed[^1] == ']') || (trimmed[0] == '(' && trimmed[^1] == ')'))
            && trimmed.IndexOfAny(['[', '(', ']', ')'], 1, trimmed.Length - 2) < 0;

        return annotation ? string.Empty : trimmed;
    }

    private WhisperProcessor GetProcessor(string prompt)
    {
        if (_processor is not null && _processorPrompt == prompt)
        {
            return _processor;
        }

        if (_factory is null)
        {
            // Library order is process-wide in Whisper.net: set it before the first model load. Vulkan falls back to the CPU.
            RuntimeOptions.RuntimeLibraryOrder = options.UseGpu ? [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu] : [RuntimeLibrary.Cpu];
            _factory = WhisperFactory.FromPath(options.ModelPath);
        }

        _processor?.Dispose();
        _processor = _factory.CreateBuilder()
            .WithLanguage("pt")
            .WithThreads(options.EffectiveThreads)
            .WithGreedySamplingStrategy(strategy => strategy.WithBestOf(1))
            .WithTemperature(0f)
            .WithPrompt(prompt)
            .Build();
        _processorPrompt = prompt;

        return _processor;
    }
}
