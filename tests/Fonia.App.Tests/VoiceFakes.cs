using Fonia.Core.Audio;
using Fonia.Core.Speech;

namespace Fonia.App.Tests;

internal sealed class FakeMicrophone : IAudioCapture
{
    public AudioFormat Format => AudioFormat.Speech;

    public bool Started { get; private set; }

    public event EventHandler<AudioDataEventArgs>? DataAvailable;

    public void StartCapture() => Started = true;

    public void StopCapture() => Started = false;

    public void Dispose()
    {
    }

    public void Feed(int milliseconds) =>
        DataAvailable?.Invoke(this, new AudioDataEventArgs(new byte[AudioFormat.Speech.BytesFor(TimeSpan.FromMilliseconds(milliseconds))]));
}

internal sealed class FakeKey : IPushToTalk
{
    public bool IsPressed { get; private set; }

    public bool Started { get; private set; }

    public event EventHandler? Pressed;

    public event EventHandler? Released;

    public void StartWatching() => Started = true;

    public void StopWatching() => Started = false;

    public void Dispose()
    {
    }

    public void Press()
    {
        IsPressed = true;
        Pressed?.Invoke(this, EventArgs.Empty);
    }

    public void Release()
    {
        IsPressed = false;
        Released?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class WarmableTranscriber(string heard, Exception? warmUpFailure = null) : ITranscriber, IWarmUp
{
    public bool WarmedUp { get; private set; }

    public List<TranscriptionContext> Contexts { get; } = [];

    public Task WarmUpAsync(TranscriptionContext context, CancellationToken ct)
    {
        Contexts.Add(context);

        if (warmUpFailure is not null)
        {
            throw warmUpFailure;
        }

        WarmedUp = true;
        return Task.CompletedTask;
    }

    public Task<Transcript> TranscribeAsync(RecordedSpeech speech, TranscriptionContext context, CancellationToken ct) =>
        Task.FromResult(new Transcript(heard, speech.Duration, TimeSpan.FromMilliseconds(1900)));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class WarmableSynthesizer : ISynthesizer, IWarmUp
{
    public bool WarmedUp { get; private set; }

    public List<string> Said { get; } = [];

    public Task WarmUpAsync(TranscriptionContext context, CancellationToken ct)
    {
        WarmedUp = true;
        return Task.CompletedTask;
    }

    public Task<SynthesizedSpeech> SynthesizeAsync(string text, Voice voice, CancellationToken ct)
    {
        Said.Add(text);
        return Task.FromResult(new SynthesizedSpeech(new float[24_000], 24_000));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class RecordingPlayer : IAudioPlayer
{
    public List<SynthesizedSpeech> Played { get; } = [];

    public Task PlayAsync(SynthesizedSpeech speech, CancellationToken ct)
    {
        Played.Add(speech);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
