using System.Runtime.InteropServices;
using Fonia.Core.Audio;
using Fonia.Core.Speech;
using NAudio.Wave;

namespace Fonia.Audio;

/// <summary>
/// The loudspeaker, through the Windows waveOut API. Windows converts the 24 kHz mono voice to whatever the device does
/// natively, so nothing here resamples. One answer at a time. Windows only.
/// </summary>
public sealed class NAudioPlayer : IAudioPlayer
{
    private readonly int _deviceNumber;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <param name="deviceNumber">Index from <see cref="ListDevices"/>, or -1 for the Windows default.</param>
    public NAudioPlayer(int deviceNumber = -1)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The loudspeaker works only on Windows.");
        }

        if (deviceNumber < -1 || deviceNumber >= WaveInterop.waveOutGetNumDevs())
        {
            throw new ArgumentOutOfRangeException(nameof(deviceNumber), deviceNumber, $"There is no loudspeaker {deviceNumber}. Use --mic-list to see them.");
        }

        _deviceNumber = deviceNumber;
    }

    /// <summary>The names of the outputs Windows knows, in the order of the device numbers.</summary>
    public static IReadOnlyList<string> ListDevices()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        return Enumerable.Range(0, WaveInterop.waveOutGetNumDevs())
            .Select(DeviceName)
            .ToList();
    }

    private static string DeviceName(int device)
    {
        var capabilities = default(WaveOutCapabilities);
        var result = (int)WaveInterop.waveOutGetDevCaps((IntPtr)device, out capabilities, Marshal.SizeOf(capabilities));

        return result == 0 ? capabilities.ProductName : $"Dispositivo {device}";
    }

    public async Task PlayAsync(SynthesizedSpeech speech, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(speech);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(ct);

        try
        {
            var pcm = speech.ToPcm16();
            using var source = new RawSourceWaveStream(new MemoryStream(pcm.Pcm), new WaveFormat(pcm.Format.SampleRate, pcm.Format.BitsPerSample, pcm.Format.Channels));
            using var output = new WaveOutEvent { DeviceNumber = _deviceNumber };
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            output.PlaybackStopped += (_, e) =>
            {
                if (e.Exception is null)
                {
                    done.TrySetResult();
                }
                else
                {
                    done.TrySetException(e.Exception);
                }
            };

            output.Init(source);
            output.Play();

            // Cancelling stops the sound; the stop ends the wait.
            await using var registration = ct.Register(output.Stop);
            await done.Task;

            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
