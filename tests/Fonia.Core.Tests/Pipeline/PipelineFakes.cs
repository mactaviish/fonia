using Fonia.Core.Audio;
using Fonia.Core.Pipeline;
using Fonia.Core.Speech;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.Core.Tests.Pipeline;

/// <summary>A recognizer that writes down what the test says it heard, and takes the time the test says.</summary>
internal sealed class FakeTranscriber(FakeTimeProvider time, Func<int, string> heard, TimeSpan? takes = null) : ITranscriber
{
    private int _calls;

    public List<TranscriptionContext> Contexts { get; } = [];

    public Task<Transcript> TranscribeAsync(RecordedSpeech speech, TranscriptionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Contexts.Add(context);
        time.Advance(takes ?? TimeSpan.Zero);

        return Task.FromResult(new Transcript(heard(_calls++), speech.Duration, takes ?? TimeSpan.Zero));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>A voice that says one second of tone, and remembers what it was asked to say and with which voice.</summary>
internal sealed class FakeSynthesizer(FakeTimeProvider time, TimeSpan? takes = null) : ISynthesizer
{
    public List<(string Text, Voice Voice)> Calls { get; } = [];

    public Func<string, Voice, SynthesizedSpeech>? Answer { get; set; }

    public Task<SynthesizedSpeech> SynthesizeAsync(string text, Voice voice, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Add((text, voice));
        time.Advance(takes ?? TimeSpan.Zero);

        return Task.FromResult(Answer?.Invoke(text, voice) ?? OneSecondOfTone());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static SynthesizedSpeech OneSecondOfTone() =>
        new(Enumerable.Range(0, 24_000).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / 24_000))).ToArray(), 24_000);
}

/// <summary>A loudspeaker that records what it was asked to play and finishes when the test lets it.</summary>
internal sealed class FakePlayer : IAudioPlayer
{
    private readonly Lock _gate = new();
    private TaskCompletionSource? _release;

    public List<SynthesizedSpeech> Played { get; } = [];

    /// <summary>When true the next playback stays open until <see cref="Finish"/>.</summary>
    public bool Hold { get; set; }

    public Task PlayAsync(SynthesizedSpeech speech, CancellationToken ct)
    {
        lock (_gate)
        {
            Played.Add(speech);

            if (!Hold)
            {
                return Task.CompletedTask;
            }

            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => _release.TrySetCanceled(ct));
            return _release.Task;
        }
    }

    public void Finish()
    {
        lock (_gate)
        {
            _release?.TrySetResult();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>An observer that writes down everything it is told, in order.</summary>
internal sealed class RecordingObserver : IVoiceLoopObserver
{
    private readonly Lock _gate = new();

    public List<string> Events { get; } = [];

    public List<string> Heard { get; } = [];

    public List<ExchangeResult> Replies { get; } = [];

    public List<Exception> Failures { get; } = [];

    public int Busies { get; private set; }

    public int TooShorts { get; private set; }

    public void OnHeard(string text) => Add($"heard:{text}", () => Heard.Add(text));

    public void OnReplied(ExchangeResult result) => Add("replied", () => Replies.Add(result));

    public void OnChannelBusy() => Add("busy", () => Busies++);

    public void OnTooShort(TimeSpan held) => Add("tooshort", () => TooShorts++);

    public void OnFailed(Exception exception) => Add("failed", () => Failures.Add(exception));

    private void Add(string name, Action action)
    {
        lock (_gate)
        {
            Events.Add(name);
            action();
        }
    }

    public async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            lock (_gate)
            {
                if (condition())
                {
                    return;
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in 10 seconds. Events: " + string.Join(", ", Events));
            }

            await Task.Delay(5, ct);
        }
    }
}
