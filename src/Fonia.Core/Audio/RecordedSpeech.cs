namespace Fonia.Core.Audio;

/// <summary>One transmission of the pilot: the raw PCM between pressing and releasing the push-to-talk.</summary>
/// <param name="Format">Layout of <paramref name="Pcm"/>.</param>
/// <param name="Pcm">The samples; this recording owns the array.</param>
public sealed record RecordedSpeech(AudioFormat Format, byte[] Pcm)
{
    public TimeSpan Duration => Format.DurationOf(Pcm.Length);
}
