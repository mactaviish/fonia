using System.Text;

namespace Fonia.Core.Phraseology;

/// <summary>
/// Reads numbers, letters and registrations the way pilots say them: digit by digit ("uno uno oito decimal uno"), by
/// words ("quinze", "vinte e cinco"), spelled with the phonetic alphabet ("papa romeo alfa") or with plain letters.
/// It accepts the manual's words (UNO, MEIA) and the everyday ones (um, seis, Romeu) alike: the NLU understands the
/// pilot, not the manual. Works on the tokens from <see cref="SpokenTokens"/>.
/// </summary>
public static class SpokenCode
{
    private const int MinimumFuzzyWordLength = 6;

    private static readonly Dictionary<string, char> DigitWords = new()
    {
        ["zero"] = '0',
        ["um"] = '1',
        ["uma"] = '1',
        ["uno"] = '1',
        ["hum"] = '1',
        ["dois"] = '2',
        ["duas"] = '2',
        ["tres"] = '3',
        ["quatro"] = '4',
        ["cinco"] = '5',
        ["seis"] = '6',
        ["meia"] = '6',
        ["sete"] = '7',
        ["oito"] = '8',
        ["nove"] = '9',
    };

    private static readonly Dictionary<string, int> Teens = new()
    {
        ["dez"] = 10,
        ["onze"] = 11,
        ["doze"] = 12,
        ["treze"] = 13,
        ["catorze"] = 14,
        ["quatorze"] = 14,
        ["quinze"] = 15,
        ["dezesseis"] = 16,
        ["dezasseis"] = 16,
        ["dezessete"] = 17,
        ["dezassete"] = 17,
        ["dezoito"] = 18,
        ["dezenove"] = 19,
        ["dezanove"] = 19,
    };

    private static readonly Dictionary<string, int> Tens = new()
    {
        ["vinte"] = 20,
        ["trinta"] = 30,
        ["quarenta"] = 40,
        ["cinquenta"] = 50,
        ["sessenta"] = 60,
        ["setenta"] = 70,
        ["oitenta"] = 80,
        ["noventa"] = 90,
    };

    private static readonly Dictionary<string, char> LetterWords = new()
    {
        ["alfa"] = 'A',
        ["alpha"] = 'A',
        ["bravo"] = 'B',
        ["charlie"] = 'C',
        ["charles"] = 'C',
        ["charly"] = 'C',
        ["charli"] = 'C',
        ["xarli"] = 'C',
        ["tcharli"] = 'C',
        ["tcharly"] = 'C',
        ["delta"] = 'D',
        ["echo"] = 'E',
        ["eco"] = 'E',
        ["foxtrot"] = 'F',
        ["golf"] = 'G',
        ["golfe"] = 'G',
        ["hotel"] = 'H',
        ["india"] = 'I',
        ["juliett"] = 'J',
        ["juliet"] = 'J',
        ["juliete"] = 'J',
        ["kilo"] = 'K',
        ["quilo"] = 'K',
        ["lima"] = 'L',
        ["mike"] = 'M',
        ["maique"] = 'M',
        ["november"] = 'N',
        ["novembro"] = 'N',
        ["oscar"] = 'O',
        ["papa"] = 'P',
        ["quebec"] = 'Q',
        ["quebeque"] = 'Q',
        ["romeo"] = 'R',
        ["romeu"] = 'R',
        ["sierra"] = 'S',
        ["tango"] = 'T',
        ["uniform"] = 'U',
        ["uniforme"] = 'U',
        ["victor"] = 'V',
        ["vitor"] = 'V',
        ["whiskey"] = 'W',
        ["whisky"] = 'W',
        ["uisque"] = 'W',
        ["xis"] = 'X',
        ["xray"] = 'X',
        ["yankee"] = 'Y',
        ["yanque"] = 'Y',
        ["zulu"] = 'Z',
    };

    /// <summary>
    /// Reads a single word the recognizer wrote for one or more spelled letters when it is not an exact phonetic word:
    /// two or more phonetic words glued together ("romeoalfa") or a long phonetic word with one letter wrong, missing
    /// or extra ("chalie", "charllie"). Short phonetic words must be exact: "tanto" is one letter from "tango".
    /// </summary>
    public static bool TryReadSpelled(string token, out string letters)
    {
        letters = string.Empty;

        if (token.Length < 4 || !token.All(char.IsAsciiLetter))
        {
            return false;
        }

        var found = new StringBuilder();

        if (!Segment(token, found))
        {
            return false;
        }

        letters = found.ToString();
        return true;
    }

    private static bool Segment(string rest, StringBuilder letters)
    {
        if (rest.Length == 0)
        {
            return true;
        }

        var mark = letters.Length;

        foreach (var (word, letter) in LetterWords.OrderByDescending(w => w.Key.Length))
        {
            if (rest.StartsWith(word, StringComparison.Ordinal))
            {
                letters.Append(letter);

                if (Segment(rest[word.Length..], letters))
                {
                    return true;
                }

                letters.Length = mark;
            }

            if (word.Length < MinimumFuzzyWordLength)
            {
                continue;
            }

            foreach (var length in new[] { word.Length - 1, word.Length + 1 })
            {
                if (length <= rest.Length && WithinOneEdit(rest[..length], word))
                {
                    letters.Append(letter);

                    if (Segment(rest[length..], letters))
                    {
                        return true;
                    }

                    letters.Length = mark;
                }
            }
        }

        return false;
    }

    // True when the words differ by one letter changed, added or removed (but are not the same).
    private static bool WithinOneEdit(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 1 || a == b)
        {
            return false;
        }

        var i = 0;

        while (i < a.Length && i < b.Length && a[i] == b[i])
        {
            i++;
        }

        return a.Length == b.Length
            ? a[(i + 1)..] == b[(i + 1)..]
            : a.Length > b.Length ? a[(i + 1)..] == b[i..] : a[i..] == b[(i + 1)..];
    }

    /// <summary>
    /// Reads one number or letter starting at <paramref name="index"/> and moves the index past what it used.
    /// "mil" reads as "000" and a decimal separator as "."; <see cref="ReadRun"/> puts them together.
    /// </summary>
    public static bool TryRead(IReadOnlyList<string> tokens, ref int index, out string value)
    {
        value = string.Empty;

        if (index < 0 || index >= tokens.Count)
        {
            return false;
        }

        var token = tokens[index];

        if (DigitWords.TryGetValue(token, out var digit))
        {
            value = digit.ToString();
        }
        else if (Teens.TryGetValue(token, out var teen))
        {
            value = teen.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (Tens.TryGetValue(token, out var tens))
        {
            return ReadTens(tokens, ref index, tens, out value);
        }
        else if (LetterWords.TryGetValue(token, out var letter))
        {
            value = letter.ToString();
        }
        else if (token == "cem")
        {
            value = "100";
        }
        else if (token == "mil")
        {
            value = "000";
        }
        else if (token == "decimal")
        {
            value = ".";
        }
        else if (token == "x" && index + 1 < tokens.Count && tokens[index + 1] == "ray")
        {
            value = "X";
            index++;
        }
        else if (token.Length == 1 && char.IsAsciiLetter(token[0]))
        {
            value = char.ToUpperInvariant(token[0]).ToString();
        }
        else if (token.All(char.IsAsciiDigit))
        {
            value = token;
        }
        else if (token.All(char.IsAsciiLetterOrDigit) && token.Any(char.IsAsciiDigit))
        {
            // "n123ab", "pt123": the speech-to-text already wrote a registration or code as one piece.
            value = token.ToUpperInvariant();
        }
        else
        {
            return false;
        }

        index++;
        return true;
    }

    /// <summary>
    /// Reads the longest sequence of numbers and letters starting at <paramref name="start"/> ("uno uno oito decimal uno"
    /// gives "118.1"). Returns an empty string when the first token is not one of them.
    /// </summary>
    public static string ReadRun(IReadOnlyList<string> tokens, int start, out int end)
    {
        var run = new StringBuilder();
        var index = start;

        while (TryRead(tokens, ref index, out var value))
        {
            run.Append(value == "000" && run.Length == 0 ? "1000" : value);
        }

        end = index;
        return run.ToString();
    }

    /// <summary>Every number and letter in the text, in order, ignoring the other words.</summary>
    public static string Decode(string? text)
    {
        var tokens = SpokenTokens.Tokenize(text);
        var decoded = new StringBuilder();
        var index = 0;

        while (index < tokens.Count)
        {
            var run = ReadRun(tokens, index, out var end);

            if (end == index)
            {
                index++;
            }
            else
            {
                decoded.Append(run);
                index = end;
            }
        }

        return decoded.ToString();
    }

    // "vinte e cinco" and "vinte cinco" are 25; "vinte e" alone is 20 and leaves the "e" for whoever comes next.
    private static bool ReadTens(IReadOnlyList<string> tokens, ref int index, int tens, out string value)
    {
        var next = index + 1;

        if (next < tokens.Count && tokens[next] == "e" && next + 1 < tokens.Count)
        {
            next++;
        }

        if (next < tokens.Count && DigitWords.TryGetValue(tokens[next], out var unit) && unit != '0')
        {
            value = (tens + (unit - '0')).ToString(System.Globalization.CultureInfo.InvariantCulture);
            index = next + 1;
            return true;
        }

        value = tens.ToString(System.Globalization.CultureInfo.InvariantCulture);
        index++;
        return true;
    }
}
