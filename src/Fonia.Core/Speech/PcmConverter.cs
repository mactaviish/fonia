using Fonia.Core.Audio;

namespace Fonia.Core.Speech;

/// <summary>Converts the microphone's PCM to the floating-point samples the recognizer takes.</summary>
public static class PcmConverter
{
    /// <summary>16-bit little-endian PCM to floats in [-1, 1].</summary>
    /// <exception cref="ArgumentException">The audio is not 16 kHz, mono, 16 bits, or ends in half a sample.</exception>
    public static float[] ToFloat(RecordedSpeech speech)
    {
        ArgumentNullException.ThrowIfNull(speech);

        if (speech.Format != AudioFormat.Speech)
        {
            throw new ArgumentException(
                $"The recognizer takes 16 kHz, mono, 16-bit audio, not {speech.Format.SampleRate} Hz, {speech.Format.Channels} channel(s), {speech.Format.BitsPerSample} bits.",
                nameof(speech));
        }

        if (speech.Pcm.Length % 2 != 0)
        {
            throw new ArgumentException("The audio ends in the middle of a sample.", nameof(speech));
        }

        var samples = new float[speech.Pcm.Length / 2];

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(speech.Pcm, i * 2) / 32768f;
        }

        return samples;
    }
}
