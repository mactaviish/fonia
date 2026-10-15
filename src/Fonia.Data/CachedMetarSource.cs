using System.Text.Json;
using Fonia.Core.Weather;

namespace Fonia.Data;

/// <summary>
/// A METAR source with a cache on disk: a report fetched less than <paramref name="freshFor"/> ago is used without asking again; when the
/// source fails, the last report is used (marked stale) as long as it was observed less than <paramref name="staleFor"/> ago, because the
/// weather of yesterday is worse than none. A cache that cannot be read or written is ignored.
/// </summary>
public sealed class CachedMetarSource(IMetarSource inner, string directory, TimeProvider timeProvider, TimeSpan freshFor, TimeSpan staleFor) : IMetarSource
{
    public async Task<MetarReading?> GetAsync(string icao, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);

        var key = new string([.. icao.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);
        var now = timeProvider.GetUtcNow();
        var cached = Read(key);

        if (cached is { } fresh && now - fresh.FetchedAt < freshFor)
        {
            return new MetarReading(fresh.Metar, fresh.ObservedAt);
        }

        var latest = await inner.GetAsync(icao, ct);

        if (latest is not null)
        {
            Write(key, latest, now);
            return latest;
        }

        return cached is { } old && now - old.ObservedAt < staleFor ? new MetarReading(old.Metar, old.ObservedAt, Stale: true) : null;
    }

    private sealed record Entry(DateTimeOffset FetchedAt, DateTimeOffset ObservedAt, string Raw);

    private (DateTimeOffset FetchedAt, DateTimeOffset ObservedAt, Metar Metar)? Read(string key)
    {
        try
        {
            var path = Path.Combine(directory, $"{key}.json");

            if (!File.Exists(path))
            {
                return null;
            }

            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));

            return entry is not null && MetarParser.TryParse(entry.Raw, out var metar) ? (entry.FetchedAt, entry.ObservedAt, metar) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void Write(string key, MetarReading reading, DateTimeOffset fetchedAt)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"{key}.json"), JsonSerializer.Serialize(new Entry(fetchedAt, reading.ObservedAt, reading.Metar.Raw)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No cache is not a failure: the next call just asks again.
        }
    }
}
