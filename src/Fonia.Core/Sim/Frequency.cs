using System.Globalization;

namespace Fonia.Core.Sim;

/// <summary>
/// A radio frequency stored as an integer number of kHz (118.275 MHz = 118275),
/// which avoids floating-point comparison problems and represents 8.33 kHz channels exactly.
/// </summary>
public readonly record struct Frequency
{
    public Frequency(int kHz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kHz);
        KHz = kHz;
    }

    public int KHz { get; }

    public double MHz => KHz / 1000.0;

    public static Frequency FromMhz(double mhz)
    {
        return new Frequency((int)Math.Round(mhz * 1000.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Formats as MHz with three decimals using a dot, e.g. "118.275".</summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{KHz / 1000}.{KHz % 1000:D3}");
    }
}
