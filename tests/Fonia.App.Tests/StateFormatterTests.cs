using Fonia.Core.Sim;
using Fonia.Sim.Mock;

namespace Fonia.App.Tests;

public class StateFormatterTests
{
    private static AircraftState Flying() => MockAircraft.Parked() with
    {
        Timestamp = new DateTimeOffset(2026, 10, 12, 19, 43, 8, TimeSpan.Zero),
        LatitudeDegrees = -27.13246,
        LongitudeDegrees = -52.66867,
        AltitudeMslFeet = 5675.4,
        AltitudeAglFeet = 3637.2,
        GroundSpeedKnots = 79.4,
        HeadingMagneticDegrees = 21,
        OnGround = false,
        Com1 = new RadioState(Frequency.FromMhz(130.85), Frequency.FromMhz(124.85)),
        Com2 = new RadioState(Frequency.FromMhz(121.5), Frequency.FromMhz(123.45)),
        TransponderCode = 1200,
        Registration = "PR-FRA",
        AircraftType = "C25C",
    };

    [Fact]
    public void A_flying_aircraft_is_one_readable_line()
    {
        var expectedTime = Flying().Timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        StateFormatter.Format(Flying()).ShouldBe(
            $"{expectedTime}  PR-FRA C25C  27.13246°S 52.66867°W  5675 ft (3637 AGL)  GS 79 kt  HDG 021°  AR  " +
            "COM1 130.850/124.850  COM2 121.500/123.450  XPDR 1200");
    }

    [Fact]
    public void The_hemisphere_follows_the_sign_of_each_coordinate()
    {
        var line = StateFormatter.Format(Flying() with { LatitudeDegrees = 51.47, LongitudeDegrees = 0.45 });

        line.ShouldContain("51.47000°N 0.45000°E");
    }

    [Fact]
    public void On_the_ground_it_says_so()
    {
        StateFormatter.Format(Flying() with { OnGround = true }).ShouldContain("  SOLO  ");
        StateFormatter.Format(Flying() with { OnGround = false }).ShouldContain("  AR  ");
    }

    [Fact]
    public void A_radio_the_aircraft_does_not_report_is_shown_as_dashes()
    {
        var line = StateFormatter.Format(Flying() with { Com2 = null });

        line.ShouldContain("COM2 --");
        line.ShouldContain("COM1 130.850/124.850");
    }

    [Theory]
    [InlineData(0.0, "000")]
    [InlineData(5.0, "005")]
    [InlineData(359.6, "000")] // rounds up to north
    [InlineData(360.0, "000")]
    [InlineData(-10.0, "350")]
    [InlineData(725.0, "005")]
    public void The_heading_is_three_digits_between_000_and_359(double heading, string expected)
    {
        StateFormatter.Format(Flying() with { HeadingMagneticDegrees = heading }).ShouldContain($"HDG {expected}°");
    }

    [Theory]
    [InlineData(0, "0000")]
    [InlineData(7, "0007")]
    [InlineData(7700, "7700")]
    public void The_squawk_always_has_four_digits(int code, string expected)
    {
        StateFormatter.Format(Flying() with { TransponderCode = code }).ShouldEndWith($"XPDR {expected}");
    }

    [Fact]
    public void A_missing_registration_or_type_does_not_leave_holes()
    {
        var line = StateFormatter.Format(Flying() with { Registration = string.Empty, AircraftType = "  " });

        line.ShouldContain("  ?  ");
    }

    // ---- fitting a narrow terminal: one line, never wider than the window ----

    [Fact]
    public void When_the_window_is_wide_enough_the_full_line_is_used()
    {
        StateFormatter.Format(Flying(), 400).ShouldBe(StateFormatter.Format(Flying()));
        StateFormatter.Format(Flying(), StateFormatter.Format(Flying()).Length).ShouldBe(StateFormatter.Format(Flying()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void An_unknown_width_means_no_limit(int width)
    {
        StateFormatter.Format(Flying(), width).ShouldBe(StateFormatter.Format(Flying()));
    }

    [Fact]
    public void A_narrower_window_drops_the_least_essential_information_first()
    {
        var full = StateFormatter.Format(Flying());

        // One column short: the standby frequencies go, everything else stays.
        var noStandby = StateFormatter.Format(Flying(), full.Length - 1);
        noStandby.ShouldNotContain("/");
        noStandby.ShouldContain("COM1 130.850");
        noStandby.ShouldContain("COM2 121.500");
        noStandby.ShouldContain("AGL");
        noStandby.ShouldContain("XPDR 1200");

        // Then the height above ground, keeping both radios.
        var noAgl = StateFormatter.Format(Flying(), noStandby.Length - 1);
        noAgl.ShouldNotContain("AGL");
        noAgl.ShouldContain("COM1 130.850");
        noAgl.ShouldContain("COM2 121.500");
        noAgl.ShouldContain("XPDR 1200");

        // Then COM2.
        var core = StateFormatter.Format(Flying(), noAgl.Length - 1);
        core.ShouldNotContain("COM2");
        core.ShouldContain("COM1 130.850");
        core.ShouldContain("XPDR 1200");

        // Then the radios, keeping where the aircraft is and how it moves.
        var minimal = StateFormatter.Format(Flying(), core.Length - 1);
        minimal.ShouldNotContain("COM");
        minimal.ShouldContain("PR-FRA");
        minimal.ShouldContain("27.13246°S 52.66867°W");
        minimal.ShouldContain("5675 ft");
        minimal.ShouldContain("GS 79 kt");
        minimal.ShouldContain("HDG 021°");
        minimal.ShouldContain("XPDR 1200");
    }

    [Fact]
    public void Below_the_smallest_layout_the_line_is_cut_with_an_ellipsis_to_exactly_the_width()
    {
        var width = StateFormatter.Format(Flying()).Length;
        var minimal = StateFormatter.Format(Flying(), width);

        // Shrink one column at a time until the layouts run out: the last one that is not an ellipsis cut.
        while (width > 1)
        {
            var next = StateFormatter.Format(Flying(), width - 1);
            if (next.EndsWith('…'))
            {
                break;
            }

            minimal = next;
            width--;
        }

        var cut = StateFormatter.Format(Flying(), minimal.Length - 1);

        cut.Length.ShouldBe(minimal.Length - 1);
        cut.ShouldEndWith("…");
        minimal.ShouldStartWith(cut[..^1]); // the same line, just shortened at the end
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void A_tiny_window_still_gets_a_line_of_exactly_that_width(int width)
    {
        var line = StateFormatter.Format(Flying(), width);
        var timeOfDay = Flying().Timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        line.Length.ShouldBe(width);
        line.ShouldEndWith("…");
        line[..^1].ShouldBe(timeOfDay[..(width - 1)]);
    }

    [Fact]
    public void For_every_window_width_the_line_never_wraps_and_never_grows_when_the_window_shrinks()
    {
        var previous = int.MaxValue;

        for (var width = 220; width >= 1; width--)
        {
            var line = StateFormatter.Format(Flying(), width);

            line.Length.ShouldBeLessThanOrEqualTo(width, $"width {width}");
            line.ShouldNotBeEmpty();
            line.ShouldNotContain("\n");
            line.Length.ShouldBeLessThanOrEqualTo(previous, $"the line grew while the window shrank at width {width}");
            previous = line.Length;
        }
    }

    [Fact]
    public void A_radio_the_aircraft_does_not_have_stays_marked_in_the_compact_layouts()
    {
        var line = StateFormatter.Format(Flying() with { Com2 = null }, StateFormatter.Format(Flying() with { Com2 = null }).Length - 1);

        line.ShouldContain("COM1");
        line.ShouldContain("COM2 --");
    }

    [Fact]
    public void The_result_does_not_depend_on_the_current_culture()
    {
        var before = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");

            var line = StateFormatter.Format(Flying());

            line.ShouldContain("27.13246°S");
            line.ShouldContain("130.850/124.850");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }
}
