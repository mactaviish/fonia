using System.Text.RegularExpressions;
using Fonia.Core.Sim;

namespace Fonia.Sim.Msfs;

/// <summary>Turns raw SimConnect values into the types of <c>Fonia.Core</c>.</summary>
internal static partial class MsfsConversions
{
    /// <summary>Hz to whole kHz, or <see langword="null"/> for a value that is not a usable frequency (zero, negative, NaN, absurd).</summary>
    public static Frequency? ToFrequency(double hz)
    {
        if (!double.IsFinite(hz) || hz <= 0)
        {
            return null;
        }

        var kHz = Math.Round(hz / 1000.0, MidpointRounding.AwayFromZero);
        return kHz is >= 1 and <= int.MaxValue ? new Frequency((int)kHz) : null;
    }

    /// <summary>
    /// A radio needs a valid active frequency; without a valid standby it reuses the active one.
    /// Returns <see langword="null"/> when the aircraft has no usable radio.
    /// </summary>
    public static RadioState? ToRadio(double activeHz, double standbyHz)
    {
        if (ToFrequency(activeHz) is not { } active)
        {
            return null;
        }

        return new RadioState(active, ToFrequency(standbyHz) ?? active);
    }

    /// <summary>
    /// Decodes a bco16 transponder code (0x1234 is squawk 1234). Squawk digits are 0 to 7, so anything else
    /// (a digit 8 or 9, a non-BCD nibble, more than four digits, a negative value) is invalid and yields 0.
    /// </summary>
    public static int ToSquawk(int bco16)
    {
        if (bco16 is < 0 or > 0xFFFF)
        {
            return 0;
        }

        var squawk = 0;

        for (var shift = 12; shift >= 0; shift -= 4)
        {
            var digit = (bco16 >> shift) & 0xF;
            if (digit > 7)
            {
                return 0;
            }

            squawk = (squawk * 10) + digit;
        }

        return squawk;
    }

    /// <summary>
    /// The aircraft type: ATC MODEL when it is a type code (<c>C25C</c>), the code inside a translation key
    /// (<c>ATCCOM.AC_MODEL_A320.0.text</c> gives <c>A320</c>), or the title when the model is empty.
    /// </summary>
    public static string ToAircraftType(string atcModel, string title)
    {
        var model = atcModel.Trim();

        if (model.Length == 0)
        {
            return title.Trim();
        }

        var key = ModelKey().Match(model);
        return key.Success ? key.Groups["type"].Value : model;
    }

    public static string ToRegistration(string atcId)
    {
        return atcId.Trim();
    }

    [GeneratedRegex(@"AC_MODEL_(?<type>[^.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ModelKey();
}
