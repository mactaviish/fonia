namespace Fonia.Speech;

/// <summary>How to run the recognizer.</summary>
/// <param name="ModelPath">The ggml model file (see <see cref="WhisperModelLocator"/>).</param>
/// <param name="UseGpu">
/// Run on the GPU through Vulkan, falling back to the CPU when it is not available. Off by default: the GPU belongs
/// to the simulator, and the recognizer only uses it in short bursts (about 0.25 s a sentence instead of 2 s on the CPU).
/// </param>
/// <param name="Threads">CPU threads; half of the cores (at most 8) when null, because the simulator needs the rest.</param>
public sealed record WhisperOptions(string ModelPath, bool UseGpu = false, int? Threads = null)
{
    public int EffectiveThreads => Threads is > 0 ? Threads.Value : Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
}
