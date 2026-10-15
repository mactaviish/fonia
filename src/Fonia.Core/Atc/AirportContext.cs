using Fonia.Core.Airports;
using Fonia.Core.Sim;
using Fonia.Core.Weather;

namespace Fonia.Core.Atc;

/// <summary>
/// What the engine needs to know about the airport it controls: who answers, which runway is in use and the weather. Fixed
/// for the session; the engine itself keeps no state.
/// </summary>
/// <param name="RunwayInUse">The runway end in use ("15"), or null when the airport data has no usable runway.</param>
/// <param name="Wind">The surface wind when the app knows it (a METAR, the simulator); null when not, and then it is not said.</param>
/// <param name="Qnh">The altimeter setting in hPa when the app knows it (a METAR); null when not, and then it is not said.</param>
/// <param name="Tuned">
/// The frequency the pilot's radio is on, when it is known (a simulator reading or the user's choice); null otherwise.
/// <see cref="Unit"/> is then who answers on it, see <see cref="FrequencyResolver"/>.
/// </param>
public sealed record AirportContext(Airport Airport, ServiceUnit Unit, string? RunwayInUse, Frequency? Tuned = null, Wind? Wind = null, int? Qnh = null)
{
    // Below this the wind says nothing about which way to land.
    private const int WindThatChoosesTheRunwayKnots = 3;

    /// <summary>
    /// The airport with the weather of a METAR: the wind said the way the controller says it (magnetic, to the ten degrees), the altimeter
    /// setting, and, unless the runway was asked for (<paramref name="keepRunway"/>), the runway with the wind most in the nose. With
    /// calm or an almost calm wind, or none, the runway stays what it was.
    /// </summary>
    public AirportContext WithWeather(Metar metar, bool keepRunway)
    {
        ArgumentNullException.ThrowIfNull(metar);

        var wind = metar.Wind is { } reported ? ToMagnetic(reported, MagneticVariation.EastDegrees(Airport)) : null;

        return this with
        {
            Wind = wind,
            Qnh = metar.QnhHectopascals,
            RunwayInUse = keepRunway || wind is null || wind.SpeedKnots < WindThatChoosesTheRunwayKnots ? RunwayInUse : RunwayWithTheWindInTheNose(wind) ?? RunwayInUse,
        };
    }

    private static Wind ToMagnetic(Wind wind, double eastVariation)
    {
        if (wind.SpeedKnots == 0)
        {
            return wind;
        }

        var magnetic = (int)Math.Round((wind.DirectionDegrees - eastVariation + 360) % 360 / 10.0, MidpointRounding.AwayFromZero) * 10;

        return new Wind(magnetic == 0 ? 360 : magnetic, wind.SpeedKnots, wind.GustKnots);
    }

    // The component of the wind along the runway, positive when it blows against the landing; the end with the most of it.
    private string? RunwayWithTheWindInTheNose(Wind wind) =>
        Airport.Runways
            .Where(r => !r.Closed)
            .SelectMany(r => r.Ends)
            .Where(e => e.MagneticHeadingDegrees is not null)
            .OrderByDescending(e => wind.SpeedKnots * Math.Cos(MagneticVariation.Normalize(wind.DirectionDegrees - e.MagneticHeadingDegrees!.Value) * Math.PI / 180))
            .Select(e => RunwayDesignator.Normalize(e.Designator))
            .FirstOrDefault();

    /// <summary>
    /// The unit that answers is what the airport really has: Solo, otherwise Torre, otherwise AFIS, otherwise nobody.
    /// The runway is the one asked for, or by default the longest, on its end with the lower number; with a METAR
    /// (<see cref="WithWeather"/>) it is the one the wind favours.
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
