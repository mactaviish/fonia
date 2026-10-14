using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Core.Atc;

/// <summary>
/// What the engine needs to know about the airport it controls: who answers and which runway is in use. Fixed for
/// the session; the engine itself keeps no state.
/// </summary>
/// <param name="RunwayInUse">The runway end in use ("15"), or null when the airport data has no usable runway.</param>
/// <param name="Wind">The surface wind when the app knows it (a METAR, the simulator); null when not, and then it is not said.</param>
/// <param name="Tuned">
/// The frequency the pilot's radio is on, when it is known (a simulator reading or the user's choice); null otherwise.
/// <see cref="Unit"/> is then who answers on it, see <see cref="FrequencyResolver"/>.
/// </param>
public sealed record AirportContext(Airport Airport, ServiceUnit Unit, string? RunwayInUse, Frequency? Tuned = null, Wind? Wind = null)
{
    /// <summary>
    /// The unit that answers is what the airport really has: Solo, otherwise Torre, otherwise AFIS, otherwise nobody.
    /// The runway is the one asked for, or by default the longest, on its end with the lower number (there is no
    /// wind or METAR to choose the other).
    /// </summary>
    /// <exception cref="ArgumentException">The requested runway is not one of the airport's open runways.</exception>
    public static AirportContext Create(Airport airport, string? requestedRunway = null)
    {
        ArgumentNullException.ThrowIfNull(airport);

        var unit = airport.HasFrequency(AirportFrequencyType.Ground) ? ServiceUnit.Ground
            : airport.HasFrequency(AirportFrequencyType.Tower) ? ServiceUnit.Tower
            : airport.HasFrequency(AirportFrequencyType.Afis) ? ServiceUnit.Afis
            : ServiceUnit.None;

        var open = airport.Runways.Where(r => !r.Closed).ToList();

        if (!string.IsNullOrWhiteSpace(requestedRunway))
        {
            var end = open.SelectMany(r => r.Ends).FirstOrDefault(e => RunwayDesignator.AreSame(e.Designator, requestedRunway));

            return end is null
                ? throw new ArgumentException(
                    $"{airport.Ident} has no open runway '{requestedRunway}' (it has: {string.Join(", ", open.Select(r => r.Name))}).",
                    nameof(requestedRunway))
                : new AirportContext(airport, unit, RunwayDesignator.Normalize(end.Designator));
        }

        var longest = open.OrderByDescending(r => r.LengthFeet ?? 0).FirstOrDefault(r => r.Ends.Any(e => RunwayDesignator.TryParse(e.Designator, out _, out _)));
        var lowerEnd = longest?.Ends
            .Select(e => RunwayDesignator.TryParse(e.Designator, out var number, out _) ? (End: e, Number: number) : (End: e, Number: int.MaxValue))
            .OrderBy(x => x.Number)
            .First().End;

        return new AirportContext(airport, unit, lowerEnd is null ? null : RunwayDesignator.Normalize(lowerEnd.Designator));
    }
}
