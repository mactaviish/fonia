using Fonia.Core.Audio;

namespace Fonia.Core.Tests.Audio;

/// <summary>A capture the test feeds by hand.</summary>
internal sealed class FakeCapture(AudioFormat? format = null) : IAudioCapture
{
    public AudioFormat Format { get; } = format ?? AudioFormat.Speech;

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    public event EventHandler<AudioDataEventArgs>? DataAvailable;

    public void StartCapture() => Started = true;

    public void StopCapture() => Started = false;

    public void Dispose() => Disposed = true;

    /// <summary>Delivers <paramref name="milliseconds"/> of audio in which every byte is <paramref name="value"/>.</summary>
    public void Feed(int milliseconds, byte value = 1)
    {
        var bytes = new byte[Format.BytesFor(TimeSpan.FromMilliseconds(milliseconds))];
        Array.Fill(bytes, value);
        RaiseData(bytes);
    }

    /// <summary>Delivers <paramref name="bytes"/> as they are, and the caller may reuse the array afterwards, like a real device.</summary>
    public void RaiseData(byte[] bytes) => DataAvailable?.Invoke(this, new AudioDataEventArgs(bytes));
}

/// <summary>A push-to-talk the test presses and releases by hand.</summary>
internal sealed class FakePushToTalk : IPushToTalk
{
    public bool IsPressed { get; private set; }

    public bool Started { get; private set; }

    public event EventHandler? Pressed;

    public event EventHandler? Released;

    public void StartWatching() => Started = true;

    public void StopWatching() => Started = false;

    public void Dispose() => Started = false;

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
