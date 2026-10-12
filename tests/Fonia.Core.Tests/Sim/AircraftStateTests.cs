using Fonia.Core.Sim;

namespace Fonia.Core.Tests.Sim;

public class AircraftStateTests
{
    private static AircraftState Sample() => new(
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

    [Fact]
    public void Equal_snapshots_are_equal_by_value()
    {
        Sample().ShouldBe(Sample());
    }

    [Fact]
    public void With_creates_a_changed_copy_and_keeps_the_original()
    {
        var original = Sample();

        var moved = original with { OnGround = false, GroundSpeedKnots = 80 };

        moved.OnGround.ShouldBeFalse();
        moved.GroundSpeedKnots.ShouldBe(80);
        original.OnGround.ShouldBeTrue();
        moved.ShouldNotBe(original);
    }
}
