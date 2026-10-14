namespace Fonia.Speech;

/// <summary>How to run the voice model.</summary>
/// <param name="ModelPath">The ONNX model (see <see cref="KokoroModelLocator"/>).</param>
/// <param name="VoicesPath">The file with the voices.</param>
/// <param name="Threads">CPU threads; half of the cores (at most 8) when null, because the simulator needs the rest.</param>
/// <param name="Speed">Speaking speed: 1.0 is normal. The model accepts 0.5 to 2.0.</param>
public sealed record KokoroOptions(string ModelPath, string VoicesPath, int? Threads = null, double Speed = 1.0)
{
    public double Speed { get; } = Speed is >= 0.5 and <= 2.0
        ? Speed
        : throw new ArgumentOutOfRangeException(nameof(Speed), Speed, "A velocidade deve ficar entre 0,5 e 2,0.");

    public int EffectiveThreads => Threads is > 0 ? Threads.Value : Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
}
