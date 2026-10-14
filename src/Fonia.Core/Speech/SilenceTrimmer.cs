namespace Fonia.Core.Speech;

/// <summary>
/// Takes the silence the voice model leaves around a sentence (it adds about a second at the start), because the
/// pilot waits for it. A little is kept so that the voice does not start or stop abruptly.
/// </summary>
public static class SilenceTrimmer
{
    // 40 dB below the loudest sample counts as silence.
    private const float RelativeThreshold = 0.01f;
    private static readonly TimeSpan KeepBefore = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan KeepAfter = TimeSpan.FromMilliseconds(80);

    public static float[] Trim(float[] samples, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var peak = samples.Length == 0 ? 0f : samples.Max(Math.Abs);

        if (peak == 0f)
        {
            return [];
        }

        var threshold = peak * RelativeThreshold;
        var first = Array.FindIndex(samples, s => Math.Abs(s) > threshold);
        var last = Array.FindLastIndex(samples, s => Math.Abs(s) > threshold);

        var start = Math.Max(0, first - (int)(KeepBefore.TotalSeconds * sampleRate));
        var end = Math.Min(samples.Length, last + 1 + (int)(KeepAfter.TotalSeconds * sampleRate));

        return samples[start..end];
    }
}
