namespace Fonia.Core.Sim;

/// <summary>
/// Optional capability of an <see cref="ISimAdapter"/>: tuning a COM radio in the simulator
/// (e.g. on a frequency handoff). Check with <c>adapter is IComRadioControl</c>.
/// </summary>
public interface IComRadioControl
{
    /// <summary>Sets the active frequency of <paramref name="radio"/>.</summary>
    Task SetComFrequencyAsync(ComRadio radio, Frequency frequency, CancellationToken ct);
}
