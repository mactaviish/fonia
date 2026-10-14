using Fonia.Core.Audio;
using NAudio.Wave;

namespace Fonia.Audio;

/// <summary>
/// A microphone through the Windows waveIn API, delivering <see cref="AudioFormat.Speech"/> (16 kHz, mono, 16 bits).
/// Windows converts from whatever the device does natively, so nothing here resamples. Windows only.
/// </summary>
public sealed class NAudioCapture : IAudioCapture
{
    private const int BufferMilliseconds = 50;

    private readonly WaveInEvent _waveIn;
    private bool _running;

    /// <param name="deviceNumber">Index from <see cref="ListDevices"/>, or -1 for the Windows default.</param>
    public NAudioCapture(int deviceNumber = -1)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The microphone capture works only on Windows.");
        }

        if (deviceNumber < -1 || deviceNumber >= WaveInEvent.DeviceCount)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceNumber), deviceNumber, $"There is no microphone {deviceNumber}. Use --mic-list to see them.");
        }

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(Format.SampleRate, Format.BitsPerSample, Format.Channels),
            BufferMilliseconds = BufferMilliseconds,
        };
        _waveIn.DataAvailable += OnData;
    }

    public AudioFormat Format { get; } = AudioFormat.Speech;

    public event EventHandler<AudioDataEventArgs>? DataAvailable;

    /// <summary>The names of the microphones Windows knows, in the order of the device numbers.</summary>
    public static IReadOnlyList<string> ListDevices()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        return Enumerable.Range(0, WaveInEvent.DeviceCount)
            .Select(i => WaveInEvent.GetCapabilities(i).ProductName)
            .ToList();
    }

    public void StartCapture()
    {
        if (_running)
        {
            return;
        }

        _waveIn.StartRecording();
        _running = true;
    }

    public void StopCapture()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _waveIn.StopRecording();
    }

    public void Dispose()
    {
        StopCapture();
        _waveIn.DataAvailable -= OnData;
        _waveIn.Dispose();
    }

    private void OnData(object? sender, WaveInEventArgs e) =>
        DataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer.AsMemory(0, e.BytesRecorded)));
}
