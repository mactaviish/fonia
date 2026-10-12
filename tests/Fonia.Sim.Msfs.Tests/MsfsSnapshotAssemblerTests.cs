using Fonia.Core.Sim;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.Sim.Msfs.Tests;

public class MsfsSnapshotAssemblerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 19, 0, 0, TimeSpan.Zero);

    // Real values from the spike (Cessna CJ4 flying near Concordia).
    private static MotionData Flying() => new(-27.13246, -52.66867, 5675, 3637, 79.4, 21, SimOnGround: 0);

    private static IdentityData Identity() => new("PR-FRA", "Cessna", "C25C", "Cessna CJ4 Citation Asobo");

    private static RadioData Radios() => new(130_850_000, 124_850_000, 124_850_000, 124_850_000);

    private static TransponderData Transponder() => new(0x1234);

    private static (MsfsSnapshotAssembler Assembler, FakeTimeProvider Clock) Create()
    {
        var clock = new FakeTimeProvider(Now);
        return (new MsfsSnapshotAssembler(clock), clock);
    }

    /// <summary>Sim running and every group delivered, so the next motion update is publishable.</summary>
    private static (MsfsSnapshotAssembler Assembler, FakeTimeProvider Clock) Ready()
    {
        var (assembler, clock) = Create();
        assembler.OnSimRunning(true);
        assembler.OnIdentity(Identity());
        assembler.OnRadios(Radios());
        assembler.OnTransponder(Transponder());
        return (assembler, clock);
    }

    [Fact]
    public void A_complete_valid_set_is_published_on_the_next_motion_update()
    {
        var (assembler, _) = Ready();

        var state = assembler.OnMotion(Flying());

        state.ShouldNotBeNull();
        state.LatitudeDegrees.ShouldBe(-27.13246);
        state.LongitudeDegrees.ShouldBe(-52.66867);
        state.AltitudeMslFeet.ShouldBe(5675);
        state.AltitudeAglFeet.ShouldBe(3637);
        state.GroundSpeedKnots.ShouldBe(79.4);
        state.HeadingMagneticDegrees.ShouldBe(21);
        state.OnGround.ShouldBeFalse();
        state.Com1.ShouldBe(new RadioState(new Frequency(130850), new Frequency(124850)));
        state.Com2.ShouldBe(new RadioState(new Frequency(124850), new Frequency(124850)));
        state.TransponderCode.ShouldBe(1234);
        state.Registration.ShouldBe("PR-FRA");
        state.AircraftType.ShouldBe("C25C");
    }

    [Fact]
    public void The_timestamp_comes_from_the_injected_clock()
    {
        var (assembler, clock) = Ready();
        clock.Advance(TimeSpan.FromSeconds(42));

        assembler.OnMotion(Flying())!.Timestamp.ShouldBe(Now.AddSeconds(42));
    }

    [Fact]
    public void On_the_ground_is_any_non_zero_value()
    {
        var (assembler, _) = Ready();

        assembler.OnMotion(Flying() with { SimOnGround = 1 })!.OnGround.ShouldBeTrue();
        assembler.OnMotion(Flying() with { SimOnGround = 0 })!.OnGround.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_is_published_while_the_simulation_is_not_running()
    {
        var (assembler, _) = Create();
        assembler.OnIdentity(Identity());
        assembler.OnRadios(Radios());
        assembler.OnTransponder(Transponder());

        assembler.OnMotion(Flying()).ShouldBeNull();
    }

    [Fact]
    public void Nothing_is_published_until_every_group_has_arrived()
    {
        var (assembler, _) = Create();
        assembler.OnSimRunning(true);

        assembler.OnMotion(Flying()).ShouldBeNull();
        assembler.OnIdentity(Identity()).ShouldBeNull();
        assembler.OnRadios(Radios()).ShouldBeNull();
        assembler.OnMotion(Flying()).ShouldBeNull(); // transponder still missing
        assembler.OnTransponder(Transponder()).ShouldNotBeNull(); // completes the set: published right away
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.00041, 0.01397)] // the placeholder MSFS reports while the flight loads
    [InlineData(-0.04, 0.04)]
    [InlineData(double.NaN, 10.0)]
    public void The_placeholder_position_while_loading_is_not_published(double latitude, double longitude)
    {
        var (assembler, _) = Ready();

        assembler.OnMotion(Flying() with { LatitudeDegrees = latitude, LongitudeDegrees = longitude }).ShouldBeNull();
        assembler.OnMotion(Flying()).ShouldNotBeNull(); // the real position arrives
    }

    [Fact]
    public void A_position_just_outside_the_placeholder_area_is_published()
    {
        var (assembler, _) = Ready();

        assembler.OnMotion(Flying() with { LatitudeDegrees = 0.06, LongitudeDegrees = 0.0 }).ShouldNotBeNull();
    }

    [Fact]
    public void A_changed_radio_is_published_immediately()
    {
        var (assembler, _) = Ready();
        assembler.OnMotion(Flying());

        var state = assembler.OnRadios(Radios() with { Com1ActiveHz = 118_100_000 });

        state.ShouldNotBeNull();
        state.Com1!.Active.ShouldBe(new Frequency(118100));
    }

    [Fact]
    public void An_unchanged_group_is_not_published_again()
    {
        var (assembler, _) = Ready();
        assembler.OnMotion(Flying());

        assembler.OnRadios(Radios()).ShouldBeNull();
        assembler.OnIdentity(Identity()).ShouldBeNull();
        assembler.OnTransponder(Transponder()).ShouldBeNull();
    }

    [Fact]
    public void A_changed_transponder_or_identity_is_published_immediately()
    {
        var (assembler, _) = Ready();
        assembler.OnMotion(Flying());

        assembler.OnTransponder(new TransponderData(0x7700))!.TransponderCode.ShouldBe(7700);
        assembler.OnIdentity(Identity() with { AtcId = "PS-WAS" })!.Registration.ShouldBe("PS-WAS");
    }

    [Fact]
    public void A_group_that_changes_before_any_motion_is_not_published()
    {
        var (assembler, _) = Ready();

        assembler.OnRadios(Radios() with { Com1ActiveHz = 118_100_000 }).ShouldBeNull();

        assembler.OnMotion(Flying())!.Com1!.Active.ShouldBe(new Frequency(118100));
    }

    [Fact]
    public void A_radio_without_a_valid_frequency_is_null_in_the_snapshot()
    {
        var (assembler, _) = Ready();
        assembler.OnRadios(new RadioData(130_850_000, 130_850_000, 0, 0));

        var state = assembler.OnMotion(Flying());

        state!.Com1.ShouldNotBeNull();
        state.Com2.ShouldBeNull();
    }

    [Fact]
    public void Stopping_the_simulation_discards_what_was_received()
    {
        var (assembler, _) = Ready();
        assembler.OnMotion(Flying()).ShouldNotBeNull();

        assembler.OnSimRunning(false);
        assembler.OnSimRunning(true);

        // A reloaded flight must not be published with the previous flight's radios and identity.
        assembler.OnMotion(Flying()).ShouldBeNull();
    }

    [Fact]
    public void After_a_reload_the_fresh_groups_are_published_again()
    {
        var (assembler, _) = Ready();
        assembler.OnSimRunning(false);
        assembler.OnSimRunning(true);

        assembler.OnIdentity(Identity() with { AtcId = "PS-WAS" });
        assembler.OnRadios(Radios());
        assembler.OnTransponder(Transponder());
        var state = assembler.OnMotion(Flying());

        state.ShouldNotBeNull();
        state.Registration.ShouldBe("PS-WAS");
    }

    [Fact]
    public void Nothing_is_published_while_the_simulation_is_stopped()
    {
        var (assembler, _) = Ready();
        assembler.OnSimRunning(false);

        assembler.OnMotion(Flying()).ShouldBeNull();
        assembler.OnRadios(Radios() with { Com1ActiveHz = 118_100_000 }).ShouldBeNull();
    }

    [Fact]
    public void Reset_forgets_everything_for_a_new_connection()
    {
        var (assembler, _) = Ready();
        assembler.OnMotion(Flying()).ShouldNotBeNull();

        assembler.Reset();

        assembler.OnMotion(Flying()).ShouldBeNull();
        assembler.OnSimRunning(true);
        assembler.OnMotion(Flying()).ShouldBeNull();
    }

    [Fact]
    public void Concurrent_updates_from_different_threads_stay_consistent()
    {
        var (assembler, _) = Ready();
        var published = 0;

        Parallel.For(0, 2000, i =>
        {
            var state = (i % 4) switch
            {
                0 => assembler.OnMotion(Flying() with { GroundSpeedKnots = i }),
                1 => assembler.OnRadios(Radios() with { Com1ActiveHz = 118_000_000 + ((i % 50) * 25_000) }),
                2 => assembler.OnTransponder(new TransponderData(0x1000 + (i % 8))),
                _ => assembler.OnIdentity(Identity() with { AtcId = $"PR-{i % 10}" }),
            };

            if (state is not null)
            {
                Interlocked.Increment(ref published);
            }
        });

        published.ShouldBeGreaterThan(0);
    }
}
