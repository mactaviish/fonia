using Fonia.Core.Audio;

namespace Fonia.Core.Speech;

/// <summary>What the synthesizer said: mono audio as floating-point samples in [-1, 1].</summary>
public sealed record SynthesizedSpeech(float[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);

    /// <summary>The same audio as 16-bit PCM, to play it or to save it with <see cref="WavEncoder"/>.</summary>
    public RecordedSpeech ToPcm16()
    {
        var pcm = new byte[Samples.Length * 2];

        for (var i = 0; i < Samples.Length; i++)
        {
            var value = (short)Math.Round(Math.Clamp(Samples[i], -1f, 1f) * short.MaxValue);
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), value);
        }

        return new RecordedSpeech(new AudioFormat(SampleRate, 1, 16), pcm);
    }
}
