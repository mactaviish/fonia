using Fonia.Core.Airports;

namespace Fonia.Core.Weather;

/// <summary>
/// The wind of a METAR is in degrees true and the controller says it in degrees magnetic (MCA 100-16, Art. 31). The airport data has no
/// magnetic variation, but each runway has its true heading and a number that is its magnetic heading to the nearest ten degrees: the
/// difference is the variation at the airport, to within a few degrees, which is all a wind said to the ten degrees needs.
/// </summary>
public static class MagneticVariation
{
    /// <summary>The variation in degrees, positive to the east (magnetic = true minus this); 0 when no runway has a true heading.</summary>
    public static double EastDegrees(Airport airport)
    {
        ArgumentNullException.ThrowIfNull(airport);

        var differences = airport.Runways
            .Where(r => !r.Closed)
            .SelectMany(r => r.Ends)
            .Where(e => e.TrueHeadingDegrees is not null && e.MagneticHeadingDegrees is not null)
            .Select(e => Normalize(e.TrueHeadingDegrees!.Value - e.MagneticHeadingDegrees!.Value))
            .ToList();

        return differences.Count == 0 ? 0 : differences.Average();
    }

    /// <summary>An angle in degrees brought to -180 to 180.</summary>
    internal static double Normalize(double degrees)
    {
        var d = degrees % 360;

        return d > 180 ? d - 360 : d <= -180 ? d + 360 : d;
    }
}
