using System.Globalization;
using System.Text.RegularExpressions;
using Fonia.Core.Sim;

namespace Fonia.Core.Weather;

/// <summary>
/// Reads the groups of a METAR the controller needs: the station, the wind, the temperature and the altimeter setting. It is
/// lenient on purpose (types, "AUTO", "COR", remarks): what it does not know it skips.
/// </summary>
public static partial class MetarParser
{
    private const double KnotsPerMetreSecond = 1.94384;
    private const double HectopascalsPerInch = 33.8639;

    public static bool TryParse(string? raw, out Metar metar)
    {
        metar = null!;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var tokens = raw.ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindIndex(tokens, t => StationGroup().IsMatch(t) && t is not ("METAR" or "SPECI" or "AUTO"));

        if (index < 0 || index + 1 >= tokens.Length || !TimeGroup().IsMatch(tokens[index + 1]))
        {
            return false;
        }

        Wind? wind = null;
        var variable = false;
        int? temperature = null;
        int? qnh = null;
        int? visibility = null;
        var cavok = false;
        var clouds = new List<CloudLayer>();
        var weather = new List<string>();
        var time = tokens[index + 1];
        var hour = int.Parse(time.AsSpan(2, 2), CultureInfo.InvariantCulture);
        var minute = int.Parse(time.AsSpan(4, 2), CultureInfo.InvariantCulture);

        foreach (var token in tokens.Skip(index + 2).TakeWhile(t => t != "RMK"))
        {
            if (WindGroup().Match(token) is { Success: true } w)
            {
                (wind, variable) = ReadWind(w);
            }
            else if (visibility is null && wind is not null | variable && VisibilityGroup().IsMatch(token))
            {
                visibility = int.Parse(token, CultureInfo.InvariantCulture);
            }
            else if (token == "CAVOK")
            {
                visibility ??= 9999;
                cavok = true;
            }
            else if (CloudGroup().Match(token) is { Success: true } c)
            {
                clouds.Add(new CloudLayer(OctasOf(c.Groups[1].Value), int.Parse(c.Groups[2].Value, CultureInfo.InvariantCulture) * 100, c.Groups[3].Value == "CB"));
            }
            else if (WeatherGroup().IsMatch(token) && !token.Contains("VC", StringComparison.Ordinal))
            {
                weather.Add(token);
            }
            else if (TemperatureGroup().Match(token) is { Success: true } t)
            {
                temperature = Signed(t.Groups[1].Value);
            }
            else if (QnhGroup().Match(token) is { Success: true } q && qnh is null)
            {
                qnh = int.Parse(q.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            else if (InchesGroup().Match(token) is { Success: true } a && qnh is null)
            {
                qnh = (int)Math.Round(int.Parse(a.Groups[1].Value, CultureInfo.InvariantCulture) / 100.0 * HectopascalsPerInch, MidpointRounding.AwayFromZero);
            }
        }

        metar = new Metar(tokens[index], wind, temperature, qnh, variable, raw.Trim(), visibility, cavok, clouds, weather, hour, minute);
        return true;
    }

    private static (Wind? Wind, bool Variable) ReadWind(Match match)
    {
        var speed = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        int? gust = match.Groups[4].Success ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : null;

        if (match.Groups[5].Value is "MPS")
        {
            speed = (int)Math.Round(speed * KnotsPerMetreSecond, MidpointRounding.AwayFromZero);
            gust = gust is { } g ? (int)Math.Round(g * KnotsPerMetreSecond, MidpointRounding.AwayFromZero) : null;
        }

        if (match.Groups[1].Value == "VRB")
        {
            return (null, true);
        }

        var direction = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

        return direction > 360
            ? (null, false)
            : (new Wind(direction, speed, gust is { } v && v >= speed ? v : null), false);
    }

    private static int OctasOf(string cover) => cover switch { "FEW" => 2, "SCT" => 4, "BKN" => 6, _ => 8 };

    // "M02" is minus two.
    private static int Signed(string value) =>
        value[0] == 'M' ? -int.Parse(value[1..], CultureInfo.InvariantCulture) : int.Parse(value, CultureInfo.InvariantCulture);

    [GeneratedRegex("^[A-Z]{4}$")]
    private static partial Regex StationGroup();

    [GeneratedRegex(@"^\d{6}Z$")]
    private static partial Regex TimeGroup();

    [GeneratedRegex(@"^(\d{3}|VRB)(\d{2,3})(G(\d{2,3}))?(KT|MPS)$")]
    private static partial Regex WindGroup();

    [GeneratedRegex(@"^(FEW|SCT|BKN|OVC)(\d{3})(CB|TCU)?$")]
    private static partial Regex CloudGroup();

    [GeneratedRegex(@"^[-+]?(?:VC)?(?:(?:MI|BC|PR|DR|BL|SH|TS|FZ)?(?:DZ|RA|SN|SG|PL|GR|GS|UP|BR|FG|FU|VA|DU|SA|HZ|PY|PO|SQ|FC|SS|DS)+|TS)$")]
    private static partial Regex WeatherGroup();

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex VisibilityGroup();

    [GeneratedRegex(@"^(M?\d{2})/(?:M?\d{2})?$")]
    private static partial Regex TemperatureGroup();

    [GeneratedRegex(@"^Q(\d{4})$")]
    private static partial Regex QnhGroup();

    [GeneratedRegex(@"^A(\d{4})$")]
    private static partial Regex InchesGroup();
}
