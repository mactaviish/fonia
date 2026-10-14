using Fonia.Speech;

namespace Fonia.App;

/// <summary>The text-to-speech command, wired to the real voice model. Only the composition root calls it.</summary>
internal static class TtsCommands
{
    public static async Task<int> SpeakAsync(AppOptions options, TextWriter output, TextWriter error, CancellationToken ct)
    {
        (string Model, string Voices) files;

        try
        {
            files = KokoroModelLocator.Resolve(options.TtsDirectory);
        }
        catch (FileNotFoundException ex)
        {
            error.WriteLine($"fonia: {ex.Message}");
            return 1;
        }

        await using var synthesizer = new KokoroSynthesizer(new KokoroOptions(files.Model, files.Voices));

        return await new SpeakCommand(synthesizer, output, error, Random.Shared)
            .RunAsync(options.SpeakText!, options.SpeakOut, options.TtsVoice, options.RadioEffect, ct);
    }
}
