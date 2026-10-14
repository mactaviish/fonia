using System.Globalization;
using System.Text;
using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Core.Phraseology;

/// <summary>
/// How the controller says digits, letters and callsigns (MCA 100-16, edition 2025, Art. 22, 23 and 36): digit by
/// digit, with UNO, MEIA and DECIMAL, and the phonetic alphabet. Output is uppercase, as in the manual.
/// </summary>
public static class SpokenText
{
    private static readonly string[] DigitNames =
        ["ZERO", "UNO", "DOIS", "TRÊS", "QUATRO", "CINCO", "MEIA", "SETE", "OITO", "NOVE"];

    private static readonly string[] LetterNames =
    [
        "ALFA", "BRAVO", "CHARLIE", "DELTA", "ECHO", "FOXTROT", "GOLF", "HOTEL", "INDIA", "JULIETT", "KILO", "LIMA", "MIKE",
        "NOVEMBER", "OSCAR", "PAPA", "QUEBEC", "ROMEO", "SIERRA", "TANGO", "UNIFORM", "VICTOR", "WHISKEY", "X-RAY", "YANKEE", "ZULU",
    ];

    // TODO(fraseologia): only the designators the manual shows as examples; the rest of the telephony table is pending.
    private static readonly Dictionary<string, string> Telephony = new(StringComparer.Ordinal)
    {
        ["FAB"] = "FORÇA AÉREA",
        ["GLO"] = "GOL",
        ["TAM"] = "TAM",
    };

    /// <summary>
    /// Digits one by one: "118.1" is "UNO UNO OITO DECIMAL UNO". With <paramref name="feminine"/> the last digit
    /// agrees with a feminine word that follows ("UMA", "DUAS").
    /// </summary>
    public static string Digits(string digits, bool feminine = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(digits);

        var words = new List<string>(digits.Length);

        for (var i = 0; i < digits.Length; i++)
        {
            var c = digits[i];

            if (c == '.')
            {
                words.Add("DECIMAL");
            }
            else if (char.IsAsciiDigit(c))
            {
                var word = DigitNames[c - '0'];

                if (feminine && i == digits.Length - 1)
                {
                    word = c switch { '1' => "UMA", '2' => "DUAS", _ => word };
                }

                words.Add(word);
            }
            else
            {
                throw new ArgumentException($"'{c}' is not a digit.", nameof(digits));
            }
        }

        return string.Join(' ', words);
    }

    /// <summary>
    /// A runway as the manual says it (Art. 29): "09" is "PISTA ZERO NOVE", "23L" is "PISTA DOIS TRÊS ESQUERDA";
    /// a side is ESQUERDA, DIREITA or CENTRO.
    /// </summary>
    public static string Runway(string designator)
    {
        if (!RunwayDesignator.TryParse(designator, out var number, out var side))
        {
            throw new ArgumentException($"'{designator}' is not a runway designator.", nameof(designator));
        }

        var sideName = side switch { 'L' => " ESQUERDA", 'R' => " DIREITA", 'C' => " CENTRO", _ => string.Empty };

        return $"PISTA {Digits(number.ToString("00", CultureInfo.InvariantCulture))}{sideName}";
    }

    /// <summary>
    /// A frequency in MHz, digit by digit, without the trailing zeros: 118.100 is "UNO UNO OITO DECIMAL UNO" and
    /// 121.500 is "UNO DOIS UNO DECIMAL CINCO".
    /// </summary>
    public static string Frequency(Frequency frequency)
    {
        var text = frequency.ToString().TrimEnd('0');

        return Digits(text.EndsWith('.') ? text + "0" : text);
    }

    /// <summary>The altimeter setting in hPa, digit by digit: 1017 is "UNO ZERO UNO SETE" and 1000 is "UNO MIL".</summary>
    public static string Qnh(int hectopascals)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hectopascals, 900);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hectopascals, 1099);

        return hectopascals == 1000 ? "UNO MIL" : Digits(hectopascals.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Letters with the phonetic alphabet and digits as digits: "PRABC" is "PAPA ROMEO ALFA BRAVO CHARLIE".</summary>
    public static string Spell(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var words = new List<string>(code.Length);

        foreach (var c in code.Where(c => c is not ('-' or ' ')))
        {
            if (char.IsAsciiLetter(c))
            {
                words.Add(LetterNames[char.ToUpperInvariant(c) - 'A']);
            }
            else if (char.IsAsciiDigit(c))
            {
                words.Add(DigitNames[c - '0']);
            }
            else
            {
                throw new ArgumentException($"'{c}' cannot be spelled.", nameof(code));
            }
        }

        return string.Join(' ', words);
    }

    /// <summary>
    /// How the controller calls an aircraft: a registration is spelled ("PR-ABC"), a company designator with a flight
    /// number or the last letters of a registration says the company's telephony first ("GLO GOW" is "GOL GOLF OSCAR
    /// WHISKEY"). With <paramref name="abbreviate"/>, a registration is cut to its last three characters, which the
    /// manual allows once contact is established; company callsigns are never abbreviated.
    /// </summary>
    public static string Callsign(string callsign, bool abbreviate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callsign);

        var code = new StringBuilder();

        foreach (var c in callsign.Where(char.IsAsciiLetterOrDigit))
        {
            code.Append(char.ToUpperInvariant(c));
        }

        var text = code.ToString();

        if (text.Length > 3 && Telephony.TryGetValue(text[..3], out var company))
        {
            return $"{company} {Spell(text[3..])}";
        }

        return Spell(abbreviate && text.Length > 3 ? text[^3..] : text);
    }

    /// <summary>
    /// Whether what the pilot said ("alfa bravo charlie", "PRABC", "pr abc") is this aircraft, given its callsign.
    /// The full callsign matches; so does its last three or more characters when they are letters, because pilots
    /// abbreviate registrations after the first contact.
    /// </summary>
    public static bool IsSameCallsign(string heard, string own)
    {
        var heardCode = Normalize(heard);
        var ownCode = Normalize(own);

        if (heardCode.Length == 0 || ownCode.Length == 0)
        {
            return false;
        }

        return heardCode == ownCode
            || (heardCode.Length >= 3 && heardCode.All(char.IsAsciiLetter) && ownCode.EndsWith(heardCode, StringComparison.Ordinal));
    }

    // Phonetic and digit words become characters ("papa romeo alfa" is PRA); the telephony of a company becomes its
    // designator ("gol" is GLO); any other word is taken as the letters themselves ("pr abc" is PRABC).
    private static string Normalize(string callsign)
    {
        var tokens = SpokenTokens.Tokenize(callsign);
        var code = new StringBuilder();
        var index = 0;

        while (index < tokens.Count)
        {
            if (SpokenCode.TryRead(tokens, ref index, out var value))
            {
                code.Append(value);
                continue;
            }

            var token = tokens[index++];

            if (token == "forca" && index < tokens.Count && tokens[index] == "aerea")
            {
                index++;
                code.Append("FAB");
            }
            else if (SpokenTelephonyDesignator(token) is { } designator)
            {
                code.Append(designator);
            }
            else if (SpokenCode.TryReadSpelled(token, out var spelled))
            {
                code.Append(spelled);
            }
            else
            {
                code.Append(token.ToUpperInvariant());
            }
        }

        return code.ToString();
    }

    private static string? SpokenTelephonyDesignator(string token) =>
        Telephony.FirstOrDefault(t => t.Key != "FAB" && string.Equals(t.Value, token, StringComparison.OrdinalIgnoreCase)).Key;
}
