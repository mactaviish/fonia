using Fonia.Core.Sim;

namespace Fonia.Sim.Mock;

/// <summary>Ready-made snapshots for tests and demos, so they don't each build an <see cref="AircraftState"/> by hand.</summary>
public static class MockAircraft
{
    /// <summary>A Cessna 172 parked on the apron at Curitiba (SBCT), engines off, on COM1 121.900.</summary>
    public static AircraftState Parked()
    {
        return new AircraftState(
            Timestamp: new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero),
            LatitudeDegrees: -25.5285,
            LongitudeDegrees: -49.1758,
            AltitudeMslFeet: 2988,
            AltitudeAglFeet: 0,
            GroundSpeedKnots: 0,
            HeadingMagneticDegrees: 150,
            OnGround: true,
            Com1: new RadioState(Frequency.FromMhz(121.9), Frequency.FromMhz(118.1)),
            Com2: new RadioState(Frequency.FromMhz(121.5), Frequency.FromMhz(121.5)),
            TransponderCode: 1200,
            Registration: "PR-ABC",
            AircraftType: "C172");
    }

    /// <summary>
    /// A straight taxi: <paramref name="steps"/> snapshots, one second apart, accelerating from
    /// standstill to 10 knots on the same heading. Position is not advanced.
    /// </summary>
    public static IReadOnlyList<AircraftState> TaxiSequence(int steps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);

        var start = Parked();

        return
        [
            .. Enumerable.Range(0, steps).Select(i => start with
            {
                Timestamp = start.Timestamp.AddSeconds(i),
                GroundSpeedKnots = steps == 1 ? 10 : 10.0 * i / (steps - 1),
            }),
        ];
    }
}
