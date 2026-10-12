using Fonia.Core.Sim;
using Fonia.Sim.Mock;

namespace Fonia.Core.Tests.Sim;

public class AircraftStateTests
{
    private static AircraftState Sample() => MockAircraft.Parked();

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
