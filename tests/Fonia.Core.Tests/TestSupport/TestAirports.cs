using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Core.Tests.TestSupport;

/// <summary>Airports built by hand for the tests: the tests of the engine never read the real data files.</summary>
internal static class TestAirports
{
    public static Runway Runway(string low, string high, int? lengthFeet, bool closed = false) =>
        new(
            new RunwayEnd(low, RunwayDesignator.MagneticHeadingDegrees(low), null, null),
            new RunwayEnd(high, RunwayDesignator.MagneticHeadingDegrees(high), null, null),
            lengthFeet,
            150,
            "ASP",
            true,
            closed);

    public static AirportFrequency Frequency(AirportFrequencyType type, int kHz) =>
        new(type, new Frequency(kHz), type.ToString());

    public static Airport Airport(string ident, IReadOnlyList<Runway> runways, params AirportFrequencyType[] frequencies) =>
        new(
            ident,
            $"{ident} Airport",
            "Curitiba",
            AirportType.Large,
            -25.5,
            -49.1,
            2000,
            ident,
            null,
            "Curitiba",
            SpokenNameSource.Reference,
            true,
            runways,
            [.. frequencies.Select((f, i) => Frequency(f, 118000 + (i * 100)))]);

    /// <summary>Two runways, 11/29 (the longer) and 15/33, with a Solo and a Torre.</summary>
    public static Airport WithSolo() =>
        Airport(
            "SBXX",
            [Runway("15", "33", 6000), Runway("29", "11", 9000)],
            AirportFrequencyType.Ground,
            AirportFrequencyType.Tower);
}
