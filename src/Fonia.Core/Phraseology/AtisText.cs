using System.Globalization;
using Fonia.Core.Atc;
using Fonia.Core.Weather;

namespace Fonia.Core.Phraseology;

/// <summary>
/// The text of the ATIS of an aerodrome, in the order of ICA 100-37, Art. 768 (combined arrival and departure broadcast), with the numbers
/// said the way the manual says them (digit by digit, UNO and MEIA). What the app has no data for is left out; see
/// <c>docs/phraseology/atis-pt-br.md</c>.
/// </summary>
public static class AtisText
{
    private const int MostLayersSaid = 3;
    private const int ShortestKilometerVisibilityMeters = 5000;
    private const int TenKilometersMeters = 9999;

    /// <summary>
    /// The designator of the broadcast (Art. 759): consecutive letters of the alphabet, starting again at 0000 UTC. It is the letter of the hour
    /// of the observation (00 is ALFA, 23 is X-RAY), so every run of the app says the same letter for the same report.
    /// </summary>
    public static string Designator(int observedHour) => SpokenText.Spell(Letter(observedHour).ToString());

    /// <summary>The letter of the designator for the hour of the observation: 'A' at 00, 'X' at 23.</summary>
    public static char Letter(int observedHour)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedHour);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(observedHour, 23);

        return (char)('A' + observedHour);
    }

    /// <summary>The letter of the designator of the report, or null when the report has no time to take it from.</summary>
    public static char? Letter(Metar metar)
    {
        ArgumentNullException.ThrowIfNull(metar);

        return metar.ObservedHour is { } hour ? Letter(hour) : null;
    }

    /// <summary>The designator of the report, or null when the report has no time to take it from.</summary>
    public static string? Designator(Metar metar)
    {
        ArgumentNullException.ThrowIfNull(metar);

        return metar.ObservedHour is { } hour ? Designator(hour) : null;
    }

    /// <summary>
    /// The combined ATIS of the airport with the weather of the report. <paramref name="airport"/> is the airport already with the weather
    /// (<see cref="AirportContext.WithWeather"/>), so the wind is the magnetic one and the runway is the one in use.
    /// </summary>
    public static string Combined(AirportContext airport, Metar metar) => string.Join(". ", Sentences(airport, metar)) + ".";

    /// <summary>The same ATIS, one sentence for each item, without the final dots: what the broadcast voices one by one.</summary>
    public static IReadOnlyList<string> Sentences(AirportContext airport, Metar metar)
    {
        ArgumentNullException.ThrowIfNull(airport);
        ArgumentNullException.ThrowIfNull(metar);

        var designator = Designator(metar);
        var parts = new List<string>
        {
            $"{airport.Airport.SpokenName}, informação {designator ?? "sem designador"}{Time(metar)}",
        };

        // TODO(fraseologia): expected approaches (II), runway conditions (IV), delays (V), transition level (VI) and other essential
        // information (VII) are left out: the app has no data for them.
        if (airport.RunwayInUse is { } runway)
        {
            parts.Add($"{SpokenText.Runway(runway)} em uso");
        }

        if (airport.Wind is { } wind)
        {
            parts.Add(SpokenText.Wind(wind));
        }

        if (metar.Cavok)
        {
            parts.Add("CAVOK");
        }
        else
        {
            if (metar.VisibilityMeters is { } visibility)
            {
                parts.Add($"visibilidade {Visibility(visibility)}");
            }

            parts.AddRange((metar.PresentWeather ?? []).Select(PresentWeather).OfType<string>());
            parts.AddRange((metar.Clouds ?? []).Take(MostLayersSaid).Select(Cloud));
        }

        if (metar.TemperatureCelsius is { } temperature)
        {
            parts.Add($"temperatura {(temperature < 0 ? "MENOS " : string.Empty)}{SpokenText.Digits(Math.Abs(temperature).ToString("00", CultureInfo.InvariantCulture))} {(Math.Abs(temperature) == 1 ? "GRAU" : "GRAUS")}");
        }

        // Art. 766: the altimeter setting is always said.
        if (metar.QnhHectopascals is { } qnh && qnh is >= 900 and <= 1099)
        {
            parts.Add($"ajuste {SpokenText.Qnh(qnh)}");
        }

        parts.Add($"informe que recebeu informação {designator ?? "recebida"}");

        return parts;
    }

    // Art. 768 I: the time of the observation, digit by digit: 12:30 is "UNO DOIS TRÊS ZERO".
    private static string Time(Metar metar) =>
        metar.ObservedHour is { } hour && metar.ObservedMinute is { } minute
            ? $", hora {SpokenText.Digits(hour.ToString("00", CultureInfo.InvariantCulture) + minute.ToString("00", CultureInfo.InvariantCulture))} ZULU"
            : string.Empty;

    // Art. 772: up to 5,000 m in meters; above that, and below 10 km, in kilometers; 10 km or more is "maior do que dez quilômetros".
    // TODO(fraseologia): the manual does not say how to round a visibility in kilometers; it is taken down to the whole kilometer.
    private static string Visibility(int meters)
    {
        if (meters >= TenKilometersMeters)
        {
            return "maior do que DEZ QUILÔMETROS";
        }

        return meters <= ShortestKilometerVisibilityMeters
            ? SpokenText.Visibility(meters)
            : $"{SpokenText.Digits((meters / 1000).ToString(CultureInfo.InvariantCulture))} {(meters / 1000 == 1 ? "QUILÔMETRO" : "QUILÔMETROS")}";
    }

    // Art. 773: the present weather in words. TODO(fraseologia): intensity ("fraca", "forte") is not in the manual.
    private static string? PresentWeather(string group)
    {
        var intensity = group[0] switch { '-' => " fraca", '+' => " forte", _ => string.Empty };
        var code = group.TrimStart('-', '+');

        var phenomenon = code switch
        {
            "TSRA" => "trovoada com chuva",
            "TS" => "trovoada",
            "SHRA" => "pancada de chuva",
            "RA" => "chuva",
            "DZ" => "chuvisco",
            "GR" => "granizo",
            "FG" => "nevoeiro",
            "BR" => "névoa úmida",
            "HZ" => "névoa seca",
            "FU" => "fumaça",
            _ => null,
        };

        return phenomenon is null ? null : phenomenon + intensity;
    }

    // Art. 774: "QUATRO OITAVOS DOIS MIL PÉS", the type only for cumulonimbus. TODO(fraseologia): a base that is no round thousand is said digit
    // by digit; the manual has only round examples.
    private static string Cloud(CloudLayer layer)
    {
        var type = layer.Cumulonimbus ? " CUMULONIMBOS" : string.Empty;
        var height = layer.BaseFeet >= 1000 && layer.BaseFeet % 1000 == 0
            ? $"{SpokenText.Digits((layer.BaseFeet / 1000).ToString(CultureInfo.InvariantCulture))} MIL PÉS"
            : $"{SpokenText.Digits(layer.BaseFeet.ToString(CultureInfo.InvariantCulture))} PÉS";

        return $"{SpokenText.Digits(layer.Octas.ToString(CultureInfo.InvariantCulture))} OITAVOS{type} {height}";
    }
}
