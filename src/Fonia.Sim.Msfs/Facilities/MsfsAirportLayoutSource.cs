using Fonia.Core.Airports;

namespace Fonia.Sim.Msfs;

/// <summary>
/// The map of an airport from the simulator that is running: it asks MSFS (<see cref="MsfsFacilityReader"/>) and makes the layout from the
/// answer (<see cref="AirportLayoutConverter"/>). An extra, like the METAR: with MSFS closed, in a menu, slow, or an airport it does not know,
/// the answer is null within <see cref="DefaultTimeout"/> and the app goes on without a map.
/// </summary>
public sealed class MsfsAirportLayoutSource : IAirportLayoutSource
{
    /// <summary>The most the app waits for the simulator before it opens without a map: the answer takes well under a second.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<string, CancellationToken, Task<AirportFacility>> _read;
    private readonly TimeSpan _timeout;

    public MsfsAirportLayoutSource(TimeSpan? timeout = null)
        : this((icao, ct) => new MsfsFacilityReader().ReadAsync(icao, ct), timeout ?? DefaultTimeout)
    {
    }

    internal MsfsAirportLayoutSource(Func<string, CancellationToken, Task<AirportFacility>> read, TimeSpan timeout)
    {
        _read = read;
        _timeout = timeout;
    }

    public async Task<AirportLayout?> GetAsync(string icao, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_timeout);

        try
        {
            return AirportLayoutConverter.Convert(await _read(icao, limit.Token)).Layout;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The simulator is closed or busy, the time ran out, or it does not know the airport: no map, and the app goes on without one.
            return null;
        }
    }
}
