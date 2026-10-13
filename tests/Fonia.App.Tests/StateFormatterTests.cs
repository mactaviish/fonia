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

    // ---- a narrow terminal: the same information, spread over as many rows as the window needs ----

    [Fact]
    public void In_a_wide_window_the_state_is_a_single_row_identical_to_the_full_line()
    {
        var rows = StateFormatter.FormatRows(Flying(), 400);

        rows.ShouldBe([StateFormatter.Format(Flying())]);
        StateFormatter.FormatRows(Flying(), StateFormatter.Format(Flying()).Length).ShouldBe([StateFormatter.Format(Flying())]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void An_unknown_width_means_a_single_row(int columns)
    {
        StateFormatter.FormatRows(Flying(), columns).ShouldBe([StateFormatter.Format(Flying())]);
    }

    [Fact]
    public void A_narrower_window_needs_more_rows_but_shows_exactly_the_same_information()
    {
        var full = StateFormatter.Format(Flying());

        var rows = StateFormatter.FormatRows(Flying(), 60);

        rows.Count.ShouldBeGreaterThan(1);
        rows.ShouldAllBe(row => row.Length <= 60);
        string.Join("  ", rows).ShouldBe(full); // nothing dropped, nothing reordered, no field split
    }

    [Fact]
    public void A_field_is_never_split_across_two_rows()
    {
        var fields = new[] { "PR-FRA C25C", "27.13246°S 52.66867°W", "5675 ft (3637 AGL)", "GS 79 kt", "HDG 021°", "AR", "COM1 130.850/124.850", "COM2 121.500/123.450", "XPDR 1200" };

        for (var columns = 21; columns <= 140; columns++)
        {
            var rows = StateFormatter.FormatRows(Flying(), columns);

            foreach (var field in fields)
            {
                rows.ShouldContain(row => row.Contains(field, StringComparison.Ordinal), $"the field '{field}' was split at {columns} columns: {string.Join(" | ", rows)}");
            }
        }
    }

    [Fact]
    public void For_every_width_no_row_is_wider_than_the_window_and_no_row_is_empty()
    {
        for (var columns = 200; columns >= 1; columns--)
        {
            var rows = StateFormatter.FormatRows(Flying(), columns);

            rows.ShouldNotBeEmpty();
            rows.ShouldAllBe(row => row.Length > 0 && row.Length <= columns, $"at {columns} columns: {string.Join(" | ", rows)}");
        }
    }

    [Fact]
    public void A_narrower_window_never_needs_fewer_rows()
    {
        var previous = 0;

        for (var columns = 200; columns >= 21; columns--)
        {
            var count = StateFormatter.FormatRows(Flying(), columns).Count;

            count.ShouldBeGreaterThanOrEqualTo(previous, $"at {columns} columns");
            previous = count;
        }
    }

    [Fact]
    public void A_window_narrower_than_a_field_still_loses_no_characters()
    {
        var withoutSpaces = StateFormatter.Format(Flying()).Replace(" ", string.Empty, StringComparison.Ordinal);

        for (var columns = 1; columns <= 20; columns++)
        {
            var rows = StateFormatter.FormatRows(Flying(), columns);

            string.Concat(rows).Replace(" ", string.Empty, StringComparison.Ordinal).ShouldBe(withoutSpaces, $"at {columns} columns");
        }
    }

    [Fact]
    public void A_radio_the_aircraft_does_not_have_stays_marked_in_every_layout()
    {
        var rows = StateFormatter.FormatRows(Flying() with { Com2 = null }, 50);

        rows.ShouldContain(row => row.Contains("COM2 --", StringComparison.Ordinal));
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
