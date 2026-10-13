using System.Globalization;
using System.Text;

namespace Fonia.Core.Phraseology;

/// <summary>
/// Turns what the speech-to-text returned (or what the pilot typed) into plain lowercase tokens: no accents, no
/// punctuation, a decimal separator between digits becomes the word "decimal". The NLU works on these tokens, so
/// "Três, um-zero!" and "tres um zero" look the same to it.
/// </summary>
public static class SpokenTokens
{
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        for (var i = 0; i < decomposed.Length; i++)
        {
            var c = decomposed[i];

            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is '.' or ',' && i > 0 && i + 1 < decomposed.Length && char.IsAsciiDigit(decomposed[i - 1]) && char.IsAsciiDigit(decomposed[i + 1]))
            {
                builder.Append(" decimal ");
            }
            else if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
