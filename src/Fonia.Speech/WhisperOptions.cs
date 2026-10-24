using Fonia.Core.Phraseology;

namespace Fonia.Speech;

/// <summary>How to run the recognizer.</summary>
/// <param name="ModelPath">The ggml model file (see <see cref="WhisperModelLocator"/>).</param>
/// <param name="UseGpu">
/// Run on the GPU through Vulkan, falling back to the CPU when it is not available. Off by default: the GPU belongs
/// to the simulator, and the recognizer only uses it in short bursts (about 0.25 s a sentence instead of 2 s on the CPU).
/// </param>
/// <param name="Threads">CPU threads; half of the cores (at most 8) when null, because the simulator needs the rest.</param>
/// <param name="Telephony">The companies the user added (a virtual airline), so that the prompt spells their callsign as the pilot says it; the table of the repository when null.</param>
public sealed record WhisperOptions(string ModelPath, bool UseGpu = false, int? Threads = null, TelephonyTable? Telephony = null)
{
    public int EffectiveThreads => Threads is > 0 ? Threads.Value : Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
}
