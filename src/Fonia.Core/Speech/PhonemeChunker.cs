namespace Fonia.Core.Speech;

/// <summary>
/// The voice model takes at most 510 phonemes at a time. A longer text is cut where a speaker would pause: after the
/// sentences, then after the commas, then between words, and in the middle of a word only if it has to be.
/// </summary>
public static class PhonemeChunker
{
    private static readonly char[][] Marks = [['.', '!', '?'], [',', ';', ':']];

    public static IReadOnlyList<string> Split(string phonemes, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(phonemes);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var text = phonemes.Trim();

        return text.Length == 0 ? [] : Chunk(text, maxLength, level: 0);
    }

    private static List<string> Chunk(string text, int max, int level)
    {
        if (text.Length <= max)
        {
            return [text];
        }

        if (level > Marks.Length)
        {
            return HardCut(text, max);
        }

        var pieces = level < Marks.Length ? SplitAfter(text, Marks[level]) : [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

        // Nothing to cut at this level: try the next one.
        if (pieces.Count == 1)
        {
            return Chunk(text, max, level + 1);
        }

        var chunks = new List<string>();
        var current = string.Empty;

        foreach (var piece in pieces)
        {
            if (current.Length > 0 && current.Length + 1 + piece.Length <= max)
            {
                current += " " + piece;
                continue;
            }

            Flush(chunks, current, max, level);
            current = piece;
        }

        Flush(chunks, current, max, level);
        return chunks;
    }

    private static void Flush(List<string> chunks, string current, int max, int level)
    {
        if (current.Length > 0)
        {
            chunks.AddRange(Chunk(current, max, level + 1));
        }
    }

    private static List<string> SplitAfter(string text, char[] marks)
    {
        var pieces = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (marks.Contains(text[i]))
            {
                AddPiece(pieces, text[start..(i + 1)]);
                start = i + 1;
            }
        }

        AddPiece(pieces, text[start..]);
        return pieces;
    }

    private static void AddPiece(List<string> pieces, string piece)
    {
        var trimmed = piece.Trim();

        if (trimmed.Length > 0)
        {
            pieces.Add(trimmed);
        }
    }

    private static List<string> HardCut(string text, int max)
    {
        var chunks = new List<string>();

        for (var i = 0; i < text.Length; i += max)
        {
            chunks.Add(text.Substring(i, Math.Min(max, text.Length - i)));
        }

        return chunks;
    }
}
