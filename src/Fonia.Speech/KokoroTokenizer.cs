namespace Fonia.Speech;

/// <summary>Turns phonemes into the numbers the Kokoro model takes.</summary>
public static class KokoroTokenizer
{
    /// <summary>The model takes at most this many phonemes at a time (without the two pads).</summary>
    public const int MaxPhonemes = 510;

    /// <summary>One number per phoneme the model knows; the others are dropped.</summary>
    public static long[] Tokenize(string phonemes)
    {
        ArgumentNullException.ThrowIfNull(phonemes);

        return [.. phonemes.Where(KokoroVocabulary.Ids.ContainsKey).Select(c => KokoroVocabulary.Ids[c])];
    }

    /// <summary>The model marks the start and the end of the sequence with a zero.</summary>
    public static long[] Pad(long[] tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return [0, .. tokens, 0];
    }
}
