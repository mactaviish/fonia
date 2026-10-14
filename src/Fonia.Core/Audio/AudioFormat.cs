namespace Fonia.Core.Audio;

/// <summary>The layout of raw PCM audio: sample rate, channels and bits per sample (little-endian integers).</summary>
public sealed record AudioFormat
{
    /// <summary>
    /// What the speech recognizer takes: 16 kHz, mono, 16 bits. The capture delivers it already in this format,
    /// so nothing converts between the microphone and the recognizer.
    /// </summary>
    public static AudioFormat Speech { get; } = new(16_000, 1, 16);

    public AudioFormat(int sampleRate, int channels, int bitsPerSample)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        if (bitsPerSample <= 0 || bitsPerSample % 8 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample), bitsPerSample, "Bits per sample must be a positive multiple of 8.");
        }

        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public int BitsPerSample { get; }

    /// <summary>Bytes of one sample for every channel.</summary>
    public int BytesPerFrame => Channels * BitsPerSample / 8;

    public int BytesPerSecond => SampleRate * BytesPerFrame;

    /// <summary>How many bytes <paramref name="duration"/> takes, rounded down to a whole frame.</summary>
    public int BytesFor(TimeSpan duration) => (int)(duration.TotalSeconds * SampleRate) * BytesPerFrame;

    /// <summary>How long <paramref name="byteCount"/> bytes of audio last.</summary>
    public TimeSpan DurationOf(int byteCount) => TimeSpan.FromSeconds((double)byteCount / BytesPerSecond);
}
