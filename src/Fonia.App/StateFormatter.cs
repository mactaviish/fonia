using System.Globalization;
using System.Text;
using Fonia.Core.Sim;

namespace Fonia.App;

/// <summary>
/// Turns an <see cref="AircraftState"/> into the text the console shows. Always culture-invariant.
/// </summary>
/// <remarks>
/// The state is a list of fields (time, aircraft, position, altitude, speed, heading, ground/air, COM1, COM2, squawk).
/// <see cref="Format(AircraftState)"/> joins them in one line; <see cref="FormatRows"/> lays the very same fields out
/// over as many rows as a window of a given width needs, never splitting a field between two rows. Nothing is ever
/// dropped: a narrow window just gets more rows.
/// </remarks>
internal static class StateFormatter
{
    private const string Separator = "  ";

    /// <summary>The whole state as a single line, whatever its width.</summary>
    public static string Format(AircraftState state)
    {
        return string.Join(Separator, Fields(state));
    }

    /// <summary>
    /// The whole state over as many rows as needed so that no row is wider than <paramref name="columns"/>; fields are
    /// kept whole and in order. A width of zero or less means "unknown": a single row.
    /// </summary>
    public static IReadOnlyList<string> FormatRows(AircraftState state, int columns)
    {
        var fields = Fields(state);

        if (columns <= 0)
        {
            return [string.Join(Separator, fields)];
        }

        var rows = new List<string>();
        var row = new StringBuilder();

        foreach (var piece in fields.SelectMany(field => Pieces(field, columns)))
        {
            if (row.Length == 0)
            {
                row.Append(piece);
            }
            else if (row.Length + Separator.Length + piece.Length <= columns)
            {
                row.Append(Separator).Append(piece);
            }
            else
            {
                rows.Add(row.ToString());
                row.Clear().Append(piece);
            }
        }

        rows.Add(row.ToString());
        return rows;
    }

    private static List<string> Fields(AircraftState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var time = state.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var aircraft = string.Join(' ', new[] { state.Registration, state.AircraftType }.Select(x => x.Trim()).Where(x => x.Length > 0));

        return
        [
            time,
            aircraft.Length > 0 ? aircraft : "?",
            string.Create(CultureInfo.InvariantCulture, $"{Coordinate(state.LatitudeDegrees, "N", "S")} {Coordinate(state.LongitudeDegrees, "E", "W")}"),
            string.Create(CultureInfo.InvariantCulture, $"{state.AltitudeMslFeet:F0} ft ({state.AltitudeAglFeet:F0} AGL)"),
            string.Create(CultureInfo.InvariantCulture, $"GS {state.GroundSpeedKnots:F0} kt"),
            string.Create(CultureInfo.InvariantCulture, $"HDG {Heading(state.HeadingMagneticDegrees):000}°"),
            state.OnGround ? "SOLO" : "AR",
            $"COM1 {Radio(state.Com1)}",
            $"COM2 {Radio(state.Com2)}",
            string.Create(CultureInfo.InvariantCulture, $"XPDR {state.TransponderCode:0000}"),
        ];
    }

    // A field wider than the window (only in an absurdly narrow one) has to be cut, or no row could ever fit it.
    private static IEnumerable<string> Pieces(string field, int columns)
    {
        for (var start = 0; start < field.Length; start += columns)
        {
            yield return field.Substring(start, Math.Min(columns, field.Length - start));
        }
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
