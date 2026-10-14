namespace Fonia.Core.Speech;

/// <summary>How much the voice sounds like it comes through a radio.</summary>
public enum RadioIntensity
{
    Off,
    Light,
    Medium,
    Strong,
}

/// <summary>
/// Makes the synthesized voice sound like a radio: the band of a small transmitter, a soft squeeze of the peaks, a faint
/// hiss, the click of the key opening and the burst of noise (squelch) when it closes. Made of plain arithmetic on the
/// samples, so it needs nothing from the system and is cheap to run.
/// </summary>
public static class RadioEffect
{
    public const RadioIntensity DefaultIntensity = RadioIntensity.Medium;

    private const double OutputPeak = 0.9;

    private static readonly TimeSpan LeadSilence = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan Click = TimeSpan.FromMilliseconds(12);
    private static readonly TimeSpan GapAfterClick = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan Squelch = TimeSpan.FromMilliseconds(120);

    private sealed record Preset(double LowHz, double HighHz, double Drive, double Hiss, double ClickLevel);

    // The values that were listened to and chosen (docs/decisions/speech.md); medium is the default.
    private static readonly Dictionary<RadioIntensity, Preset> Presets = new()
    {
        [RadioIntensity.Light] = new(250, 3800, 1.5, 0.004, 0.10),
        [RadioIntensity.Medium] = new(350, 3200, 2.5, 0.010, 0.18),
        [RadioIntensity.Strong] = new(450, 2800, 4.0, 0.022, 0.28),
    };

    /// <summary>Reads the names the user may type: desligado, leve, média, forte (or off, light, medium, strong).</summary>
    public static bool TryParse(string? text, out RadioIntensity intensity)
    {
        intensity = DefaultIntensity;

        switch (text?.Trim().ToLowerInvariant().Replace("é", "e", StringComparison.Ordinal))
        {
            case "desligado" or "off" or "nenhum":
                intensity = RadioIntensity.Off;
                return true;
            case "leve" or "light":
                intensity = RadioIntensity.Light;
                return true;
            case "media" or "medium":
                intensity = RadioIntensity.Medium;
                return true;
            case "forte" or "strong":
                intensity = RadioIntensity.Strong;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The voice through the radio. The result is longer than the voice: it has the click before and the squelch after.</summary>
    /// <param name="random">Where the noise comes from; a fixed seed makes the result repeatable.</param>
    public static float[] Apply(float[] samples, int sampleRate, RadioIntensity intensity, Random random)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(random);

        if (intensity == RadioIntensity.Off || samples.Length == 0)
        {
            return [.. samples];
        }

        var preset = Presets[intensity];
        var voice = Squeeze(RadioFilter.BandPass(Normalize(samples, 1.0), sampleRate, preset.LowHz, preset.HighHz), preset.Drive);
        var hiss = RadioFilter.BandPass(WhiteNoise(voice.Length, random), sampleRate, preset.LowHz, preset.HighHz);

        for (var i = 0; i < voice.Length; i++)
        {
            voice[i] += (float)(hiss[i] * preset.Hiss);
        }

        var result = new List<float>(voice.Length + (sampleRate / 4));
        result.AddRange(new float[Samples(LeadSilence, sampleRate)]);
        result.AddRange(KeyClick(Samples(Click, sampleRate), preset.ClickLevel, random));
        result.AddRange(new float[Samples(GapAfterClick, sampleRate)]);
        result.AddRange(voice);
        result.AddRange(SquelchBurst(Samples(Squelch, sampleRate), preset, sampleRate, random));

        return Normalize([.. result], OutputPeak);
    }

    private static int Samples(TimeSpan time, int sampleRate) => (int)(time.TotalSeconds * sampleRate);

    // tanh squeezes the peaks and rounds them: the "crunch" of a transmitter, stronger as the drive grows.
    private static float[] Squeeze(float[] samples, double drive)
    {
        var scale = Math.Tanh(drive);

        return Array.ConvertAll(samples, s => (float)(Math.Tanh(s * drive) / scale));
    }

    private static float[] Normalize(float[] samples, double peakTarget)
    {
        var peak = samples.Max(Math.Abs);

        if (peak == 0f)
        {
            return [.. samples];
        }

        var gain = (float)(peakTarget / peak);
        return Array.ConvertAll(samples, s => s * gain);
    }

    private static float[] WhiteNoise(int count, Random random)
    {
        var noise = new float[count];

        for (var i = 0; i < count; i++)
        {
            // Box-Muller: a Gaussian from two uniform numbers.
            var u1 = 1.0 - random.NextDouble();
            var u2 = random.NextDouble();
            noise[i] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        return noise;
    }

    // A short burst of noise under a Hann window: the sound of the key being pressed.
    private static float[] KeyClick(int count, double level, Random random)
    {
        var noise = WhiteNoise(count, random);

        for (var i = 0; i < count; i++)
        {
            var window = 0.5 - (0.5 * Math.Cos(2.0 * Math.PI * i / Math.Max(1, count - 1)));
            noise[i] = (float)(noise[i] * window * level);
        }

        return noise;
    }

    // The noise a receiver makes when the carrier drops: filtered like the voice and dying out.
    private static float[] SquelchBurst(int count, Preset preset, int sampleRate, Random random)
    {
        var noise = RadioFilter.BandPass(WhiteNoise(count, random), sampleRate, preset.LowHz, preset.HighHz);

        for (var i = 0; i < count; i++)
        {
            var fade = 1.0 - ((double)i / count);
            noise[i] = (float)(noise[i] * fade * preset.ClickLevel * 0.8);
        }

        return noise;
    }
}
