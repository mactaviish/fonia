using System.Net;
using System.Text.Json;
using Fonia.Core.Weather;

namespace Fonia.Data;

/// <summary>
/// The latest METAR of an airport from aviationweather.gov (NOAA, public data, no key). Only the ICAO code goes to the network.
/// Any failure of the network, a timeout or a reply it cannot read is "no METAR" (null), never an exception: the weather is an extra,
/// and the radio works without it.
/// </summary>
public sealed class AviationWeatherMetarSource(HttpClient http, TimeProvider? timeProvider = null, string baseUrl = AviationWeatherMetarSource.DefaultUrl) : IMetarSource
{
    public const string DefaultUrl = "https://aviationweather.gov/api/data/metar";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<MetarReading?> GetAsync(string icao, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);

        try
        {
            using var response = await http.GetAsync(new Uri($"{baseUrl}?ids={Uri.EscapeDataString(icao.Trim().ToUpperInvariant())}&format=json"), ct);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            return Read(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    // [{"icaoId":"SBCT","obsTime":1760529600,"rawOb":"SBCT 151200Z ..."}]
    private MetarReading? Read(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var report = document.RootElement[0];

        if (report.ValueKind != JsonValueKind.Object
            || !report.TryGetProperty("rawOb", out var raw)
            || raw.ValueKind != JsonValueKind.String
            || !MetarParser.TryParse(raw.GetString(), out var metar))
        {
            return null;
        }

        var observed = report.TryGetProperty("obsTime", out var time) && time.ValueKind == JsonValueKind.Number && time.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : _time.GetUtcNow();

        return new MetarReading(metar, observed);
    }
}
