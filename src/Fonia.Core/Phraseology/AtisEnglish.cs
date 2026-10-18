using System.Globalization;
using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Weather;

namespace Fonia.Core.Phraseology;

/// <summary>
/// The ATIS in English, the second half of the broadcast (ICA 100-37, Art. 761 §2º: Portuguese and English). Same items, same order as
/// <see cref="AtisText"/>, in the ICAO style (digits one by one: "tree", "fife", "niner"). The words are all in <see cref="Speech.EnglishPhonemizer"/>
/// but the name of the airport.
/// </summary>
// TODO(fraseologia): the ICA gives no English example; the phrases follow the usual ICAO ATIS ("advise on initial contact you have information ...").
public static class AtisEnglish
{
    private const int MostLayersSaid = 3;
    private const int ShortestKilometerVisibilityMeters = 5000;
    private const int TenKilometersMeters = 9999;

    private static readonly string[] DigitNames = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

    /// <summary>The sentences of the ATIS in English, in the order of Art. 768; what the app has no data for is left out, as in Portuguese.</summary>
    public static IReadOnlyList<string> Sentences(AirportContext airport, Metar metar)
    {
        ArgumentNullException.ThrowIfNull(airport);
        ArgumentNullException.ThrowIfNull(metar);

        var designator = AtisText.Letter(metar) is { } letter ? Phonetic(letter) : null;
        var time = metar.ObservedHour is { } hour && metar.ObservedMinute is { } minute
            ? $", time {Digits(hour.ToString("00", CultureInfo.InvariantCulture) + minute.ToString("00", CultureInfo.InvariantCulture))} zulu"
            : string.Empty;
        var parts = new List<string> { $"{airport.Airport.SpokenName} information {designator ?? "received"}{time}" };

        if (airport.RunwayInUse is { } runway && RunwayDesignator.TryParse(runway, out var number, out var side))
        {
            var sideName = side switch { 'L' => " left", 'R' => " right", 'C' => " centre", _ => string.Empty };
            parts.Add($"runway {Digits(number.ToString("00", CultureInfo.InvariantCulture))}{sideName} in use");
        }

        if (airport.Wind is { } wind)
        {
            parts.Add(Wind(wind));
        }

        if (metar.Cavok)
        {
            parts.Add("CAVOK");
        }
        else
        {
            if (metar.VisibilityMeters is { } visibility)
            {
                parts.Add($"visibility {Visibility(visibility)}");
            }

            parts.AddRange((metar.PresentWeather ?? []).Select(PresentWeather).OfType<string>());
            parts.AddRange((metar.Clouds ?? []).Take(MostLayersSaid).Select(Cloud));
        }

        if (metar.TemperatureCelsius is { } temperature)
        {
            parts.Add($"temperature {(temperature < 0 ? "minus " : string.Empty)}{Digits(Math.Abs(temperature).ToString("00", CultureInfo.InvariantCulture))}");
        }

        if (metar.QnhHectopascals is { } qnh && qnh is >= 900 and <= 1099)
        {
            parts.Add($"QNH {(qnh == 1000 ? "one thousand" : Digits(qnh.ToString(CultureInfo.InvariantCulture)))}");
        }

        parts.Add($"advise on initial contact you have information {designator ?? "received"}");

        return parts;
    }

    // Digit by digit, ICAO style: "1230" is "one two three zero".
    private static string Digits(string digits) =>
        string.Join(' ', digits.Select(c => DigitNames[c - '0']));

    private static string Phonetic(char letter) => letter switch
    {
        'A' => "alfa",
        'B' => "bravo",
        'C' => "charlie",
        'D' => "delta",
        'E' => "echo",
        'F' => "foxtrot",
        'G' => "golf",
        'H' => "hotel",
        'I' => "india",
        'J' => "juliett",
        'K' => "kilo",
        'L' => "lima",
        'M' => "mike",
        'N' => "november",
        'O' => "oscar",
        'P' => "papa",
        'Q' => "quebec",
        'R' => "romeo",
        'S' => "sierra",
        'T' => "tango",
        'U' => "uniform",
        'V' => "victor",
        'W' => "whiskey",
        'X' => "x-ray",
        'Y' => "yankee",
        _ => "zulu",
    };

    private static string Wind(Sim.Wind wind)
    {
        if (wind.SpeedKnots < 1)
        {
            return "wind calm";
        }

        var direction = (wind.DirectionDegrees == 0 ? 360 : wind.DirectionDegrees).ToString("000", CultureInfo.InvariantCulture);
        var text = $"wind {Digits(direction)} degrees {Digits(wind.SpeedKnots.ToString(CultureInfo.InvariantCulture))} knots";

        return wind.GustKnots is { } gust ? $"{text}, gusting {Digits(gust.ToString(CultureInfo.InvariantCulture))}" : text;
    }

    // Same limits as Art. 772: meters up to 5,000, kilometers to 10 km, and from there "more than ten kilometers".
    private static string Visibility(int meters)
    {
        if (meters >= TenKilometersMeters)
        {
            return "more than ten kilometers";
        }

        return meters <= ShortestKilometerVisibilityMeters
            ? Thousands(meters) + " meters"
            : $"{Digits((meters / 1000).ToString(CultureInfo.InvariantCulture))} kilometers";
    }

    // MCA 100-16, Art. 32, §1 and §2 (English): round hundreds and thousands are said with the words THOUSAND and HUNDRED:
    // "2000" is "two thousand", "2300" is "two thousand three hundred", "700" is "seven hundred"; any other number digit by digit.
    private static string Thousands(int value)
    {
        if (value < 100 || value % 100 != 0)
        {
            return Digits(value.ToString(CultureInfo.InvariantCulture));
        }

        var thousands = value / 1000 > 0 ? $"{Digits((value / 1000).ToString(CultureInfo.InvariantCulture))} thousand" : string.Empty;
        var hundreds = value % 1000 / 100 > 0 ? $"{Digits((value % 1000 / 100).ToString(CultureInfo.InvariantCulture))} hundred" : string.Empty;

        return $"{thousands} {hundreds}".Trim();
    }

    private static string? PresentWeather(string group)
    {
        var intensity = group[0] switch { '-' => "light ", '+' => "heavy ", _ => string.Empty };

        var phenomenon = group.TrimStart('-', '+') switch
        {
            "TSRA" => "thunderstorm with rain",
            "TS" => "thunderstorm",
            "SHRA" => "shower of rain",
            "RA" => "rain",
            "DZ" => "drizzle",
            "GR" => "hail",
            "FG" => "fog",
            "BR" => "mist",
            "HZ" => "haze",
            "FU" => "smoke",
            _ => null,
        };

        return phenomenon is null ? null : intensity + phenomenon;
    }

    private static string Cloud(CloudLayer layer) =>
        $"{Digits(layer.Octas.ToString(CultureInfo.InvariantCulture))} oktas{(layer.Cumulonimbus ? " cumulonimbus" : string.Empty)} {Thousands(layer.BaseFeet)} feet";
}
