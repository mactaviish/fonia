using System.Globalization;
using Fonia.Core.Sim;

namespace Fonia.App;

/// <summary>Turns an <see cref="AircraftState"/> into the single line the console shows. Always culture-invariant.</summary>
internal static class StateFormatter
{
    public static string Format(AircraftState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var time = state.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var aircraft = string.Join(' ', new[] { state.Registration, state.AircraftType }.Select(x => x.Trim()).Where(x => x.Length > 0));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{time}  {(aircraft.Length > 0 ? aircraft : "?")}  " +
            $"{Coordinate(state.LatitudeDegrees, "N", "S")} {Coordinate(state.LongitudeDegrees, "E", "W")}  " +
            $"{state.AltitudeMslFeet:F0} ft ({state.AltitudeAglFeet:F0} AGL)  GS {state.GroundSpeedKnots:F0} kt  " +
            $"HDG {Heading(state.HeadingMagneticDegrees):000}°  {(state.OnGround ? "SOLO" : "AR")}  " +
            $"COM1 {Radio(state.Com1)}  COM2 {Radio(state.Com2)}  XPDR {state.TransponderCode:0000}");
    }

    private static string Coordinate(double degrees, string positive, string negative)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(degrees):F5}°{(degrees >= 0 ? positive : negative)}");
    }

    // 0 to 359, rounded to the nearest degree (359.6 is north, not "360").
    private static int Heading(double degrees)
    {
        var rounded = (int)Math.Round(((degrees % 360) + 360) % 360, MidpointRounding.AwayFromZero);
        return rounded % 360;
    }

    private static string Radio(RadioState? radio)
    {
        return radio is null ? "--" : $"{radio.Active}/{radio.Standby}";
    }
}
