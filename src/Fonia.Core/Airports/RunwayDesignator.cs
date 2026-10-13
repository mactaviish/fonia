using System.Globalization;

namespace Fonia.Core.Airports;

/// <summary>
/// Runway designators ("15", "09R", "33L"): a number from 1 to 36 (the magnetic heading divided by ten, rounded)
/// and, for parallel runways, a side. Anything else (a helipad "H1", a letter) is not a runway designator.
/// </summary>
public static class RunwayDesignator
{
    public static bool TryParse(string? text, out int number, out char? side)
    {
        number = 0;
        side = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var designator = text.Trim().ToUpperInvariant();
        var digitsEnd = designator.Length;
        char? parsedSide = null;

        if (designator[^1] is 'L' or 'R' or 'C')
        {
            parsedSide = designator[^1];
            digitsEnd--;
        }

        if (digitsEnd is < 1 or > 2 || !designator[..digitsEnd].All(char.IsAsciiDigit))
        {
            return false;
        }

        var parsedNumber = int.Parse(designator[..digitsEnd], CultureInfo.InvariantCulture);

        if (parsedNumber is < 1 or > 36)
        {
            return false;
        }

        number = parsedNumber;
        side = parsedSide;
        return true;
    }

    /// <summary>
    /// The approximate magnetic heading of the runway, from its number ("15L" is 150°), or <see langword="null"/> if
    /// the text is not a runway designator. The airport data rarely carries the heading, and the number already is one.
    /// </summary>
    public static double? MagneticHeadingDegrees(string? designator)
    {
        return TryParse(designator, out var number, out _) ? number * 10.0 : null;
    }

    /// <summary>
    /// Two digits and an upper-case side ("9r" becomes "09R"). Text that is not a runway designator is only trimmed
    /// and upper-cased, so it can still be compared.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (TryParse(text, out var number, out var side))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{number:00}{side}");
        }

        return text?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    public static bool AreSame(string? a, string? b)
    {
        return string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
    }
}
