namespace Fonia.Core.Weather;

/// <summary>A METAR and when it was observed. <paramref name="Stale"/> is true when it comes from the cache after the source failed.</summary>
public sealed record MetarReading(Metar Metar, DateTimeOffset ObservedAt, bool Stale = false);

/// <summary>Where the weather of an airport comes from. Implementations live outside the core (the network, the cache).</summary>
public interface IMetarSource
{
    /// <returns>The latest METAR of the airport, or null when there is none (no report, no network).</returns>
    Task<MetarReading?> GetAsync(string icao, CancellationToken ct);
}
