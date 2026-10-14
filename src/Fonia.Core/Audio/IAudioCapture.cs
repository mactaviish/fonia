namespace Fonia.Core.Audio;

/// <summary>A microphone: delivers raw PCM in <see cref="Format"/> while it is running.</summary>
public interface IAudioCapture : IDisposable
{
    AudioFormat Format { get; }

    /// <summary>
    /// Raised on a thread of the device, in order, every few tens of milliseconds while running.
    /// The data is only valid during the handler: devices reuse their buffers, so copy what you keep.
    /// </summary>
    event EventHandler<AudioDataEventArgs>? DataAvailable;

    /// <summary>Starts delivering audio. Does nothing if it is already running.</summary>
    void StartCapture();

    /// <summary>Stops delivering audio. Does nothing if it is not running.</summary>
    void StopCapture();
}

/// <summary>A block of PCM from an <see cref="IAudioCapture"/>.</summary>
public sealed class AudioDataEventArgs(ReadOnlyMemory<byte> data) : EventArgs
{
    public ReadOnlyMemory<byte> Data { get; } = data;
}
