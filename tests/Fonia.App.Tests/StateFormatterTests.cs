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
