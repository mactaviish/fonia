using System.Globalization;
using Fonia.Core.Sim;

namespace Fonia.App;

/// <summary>
/// Turns an <see cref="AircraftState"/> into the single line the console shows. Always culture-invariant.
/// </summary>
/// <remarks>
/// On a terminal the line is rewritten in place, so it must never be wider than the window (it would wrap and
/// stop being one line). <see cref="Format(AircraftState, int)"/> therefore picks the richest layout that fits and,
/// as the window narrows, drops the least essential information first: the standby frequencies, then the height above
/// ground, then COM2, then the radios. Position, altitude, speed, heading, ground/air and the squawk are kept as long
/// as possible; below that the line is cut with an ellipsis.
/// </remarks>
internal static class StateFormatter
{
    private const string Ellipsis = "…";

    // From the richest to the most compact. Each one is strictly shorter than the one before it.
    private static readonly Layout[] Layouts =
    [
        new(ShowAgl: true, Radios: RadioDetail.ActiveAndStandby),
        new(ShowAgl: true, Radios: RadioDetail.ActiveOnly),
        new(ShowAgl: false, Radios: RadioDetail.ActiveOnly),
        new(ShowAgl: false, Radios: RadioDetail.Com1ActiveOnly),
        new(ShowAgl: false, Radios: RadioDetail.None),
    ];

    private enum RadioDetail
    {
        ActiveAndStandby,
        ActiveOnly,
        Com1ActiveOnly,
        None,
    }

    /// <summary>The full line, with no limit on its width.</summary>
    public static string Format(AircraftState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return Build(state, Layouts[0]);
    }

    /// <summary>
    /// The richest line that is at most <paramref name="maxWidth"/> characters wide. A width of zero or less means
    /// "unknown": no limit.
    /// </summary>
    public static string Format(AircraftState state, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (maxWidth <= 0)
        {
            return Format(state);
        }

        foreach (var layout in Layouts)
        {
            var line = Build(state, layout);

            if (line.Length <= maxWidth)
            {
                return line;
            }
        }

        var smallest = Build(state, Layouts[^1]);
        return maxWidth == 1 ? Ellipsis : string.Concat(smallest.AsSpan(0, maxWidth - 1), Ellipsis);
    }

    private static string Build(AircraftState state, Layout layout)
    {
        var time = state.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var aircraft = string.Join(' ', new[] { state.Registration, state.AircraftType }.Select(x => x.Trim()).Where(x => x.Length > 0));

        var parts = new List<string>
        {
            time,
            aircraft.Length > 0 ? aircraft : "?",
            string.Create(CultureInfo.InvariantCulture, $"{Coordinate(state.LatitudeDegrees, "N", "S")} {Coordinate(state.LongitudeDegrees, "E", "W")}"),
            layout.ShowAgl
                ? string.Create(CultureInfo.InvariantCulture, $"{state.AltitudeMslFeet:F0} ft ({state.AltitudeAglFeet:F0} AGL)")
                : string.Create(CultureInfo.InvariantCulture, $"{state.AltitudeMslFeet:F0} ft"),
            string.Create(CultureInfo.InvariantCulture, $"GS {state.GroundSpeedKnots:F0} kt"),
            string.Create(CultureInfo.InvariantCulture, $"HDG {Heading(state.HeadingMagneticDegrees):000}°"),
            state.OnGround ? "SOLO" : "AR",
        };

        switch (layout.Radios)
        {
            case RadioDetail.ActiveAndStandby:
                parts.Add($"COM1 {Radio(state.Com1, withStandby: true)}");
                parts.Add($"COM2 {Radio(state.Com2, withStandby: true)}");
                break;
            case RadioDetail.ActiveOnly:
                parts.Add($"COM1 {Radio(state.Com1, withStandby: false)}");
                parts.Add($"COM2 {Radio(state.Com2, withStandby: false)}");
                break;
            case RadioDetail.Com1ActiveOnly:
                parts.Add($"COM1 {Radio(state.Com1, withStandby: false)}");
                break;
            case RadioDetail.None:
                break;
        }

        parts.Add(string.Create(CultureInfo.InvariantCulture, $"XPDR {state.TransponderCode:0000}"));

        return string.Join("  ", parts);
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

    private static string Radio(RadioState? radio, bool withStandby)
    {
        if (radio is null)
        {
            return "--";
        }

        return withStandby ? $"{radio.Active}/{radio.Standby}" : radio.Active.ToString();
    }

    private readonly record struct Layout(bool ShowAgl, RadioDetail Radios);
}
