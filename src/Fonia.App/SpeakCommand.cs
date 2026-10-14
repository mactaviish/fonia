using System.Diagnostics;
using System.Globalization;
using Fonia.Core.Audio;
using Fonia.Core.Speech;

namespace Fonia.App;

/// <summary>
/// <c>--speak</c>: says a text with the controller's voice and the radio effect and saves it as a WAV file. It is the way
/// to hear how the controller sounds, before the voice is part of the loop with the pilot.
/// </summary>
internal sealed class SpeakCommand(ISynthesizer synthesizer, TextWriter output, TextWriter error, Random random)
{
    /// <param name="voiceId">The voice, or null for one of the approved voices chosen at random.</param>
    /// <returns>0 when the file was written, 1 when it could not be made, 2 when the voice does not exist.</returns>
    public async Task<int> RunAsync(string text, string path, string? voiceId, RadioIntensity effect, CancellationToken ct)
    {
        Voice voice;

        if (voiceId is null)
        {
            voice = new VoiceSelector(VoiceCatalog.Default, null, random).For("fonia --speak");
        }
        else if (VoiceCatalog.Find(voiceId) is { } found)
        {
            voice = found;
        }
        else
        {
            error.WriteLine($"fonia: a voz '{voiceId}' não existe. Use: {string.Join(", ", VoiceCatalog.Default.Select(v => v.Id))}.");
            return 2;
        }

        output.WriteLine($"Voz: {voice.Name} ({voice.Id}) | efeito de rádio: {EffectName(effect)}");
        output.WriteLine("Falando (na primeira vez o modelo é carregado, o que leva alguns segundos)...");

        SynthesizedSpeech speech;
        var watch = Stopwatch.StartNew();

        try
        {
            speech = await synthesizer.SynthesizeAsync(text, voice, ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or InvalidDataException or KeyNotFoundException)
        {
            error.WriteLine(ex.Message.StartsWith("fonia:", StringComparison.Ordinal) ? ex.Message : $"fonia: {ex.Message}");
            return 1;
        }

        watch.Stop();
        var radio = new SynthesizedSpeech(RadioEffect.Apply(speech.Samples, speech.SampleRate, effect, random), speech.SampleRate);

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(path, WavEncoder.Encode(radio.ToPcm16()), CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Não foi possível gravar '{path}': {ex.Message}");
            return 1;
        }

        output.WriteLine($"Fala: {Seconds(radio.Duration)} s | síntese: {Seconds(watch.Elapsed)} s");
        output.WriteLine($"Gravado em {path}");
        return 0;
    }

    private static string EffectName(RadioIntensity effect) => effect switch
    {
        RadioIntensity.Off => "desligado",
        RadioIntensity.Light => "leve",
        RadioIntensity.Medium => "média",
        _ => "forte",
    };

    private static string Seconds(TimeSpan duration) =>
        duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
}
