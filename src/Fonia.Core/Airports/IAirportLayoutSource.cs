namespace Fonia.Core.Airports;

/// <summary>
/// Where the map of an airport comes from (the simulator, or what was kept from it). Like the METAR, it is an extra: when it cannot be had the
/// answer is null and the app says the runway and nothing more, as it did before the map existed.
/// </summary>
public interface IAirportLayoutSource
{
    /// <returns>The layout of the airport, or null when there is none to be had. Never throws for anything but cancellation.</returns>
    Task<AirportLayout?> GetAsync(string icao, CancellationToken ct);
}
