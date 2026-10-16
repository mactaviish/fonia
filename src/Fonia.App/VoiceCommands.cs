using Fonia.Audio;
using Fonia.Core.Atc;
using Fonia.Core.Audio;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Speech;
using Fonia.Speech;

namespace Fonia.App;

/// <summary>The voice mode of <c>--atc --voice</c>, wired to the real microphone, key, models and loudspeaker. Only the composition root calls it.</summary>
internal static class VoiceCommands
{
    /// <summary>The voice mode and the devices it must close, or null (with the reason on <paramref name="error"/>) when it cannot be built.</summary>
    public static async Task<VoiceModeHandle?> TryCreateAsync(AppOptions options, TextReader input, TextWriter output, TextWriter error, bool interactive, Func<Fonia.Core.Sim.Frequency?> com1, Func<CancellationToken, IAsyncEnumerable<Fonia.Core.Sim.AircraftState>?> states, Func<AirportContext, string, CaseLog?>? cases, Func<Fonia.Core.Sim.AircraftState?> aircraft, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            error.WriteLine("fonia: a voz (microfone e alto-falante) só funciona no Windows.");
            return null;
        }

        if (!PushToTalkKey.TryParse(options.PttKey, out var virtualKey))
        {
            error.WriteLine($"fonia: tecla '{options.PttKey}' desconhecida.");
            return null;
        }

        // Before anything is opened or loaded: if a model is missing, this is the moment to offer to download it.
        if (!await ModelCommands.EnsureAsync(ModelNeeds.ForVoice(options), input, output, error, interactive, ct))
        {
            return null;
        }

        string sttModel;
        (string Model, string Voices) ttsFiles;

        try
        {
            sttModel = WhisperModelLocator.Resolve(options.SttDirectory, options.SttModel);
            ttsFiles = KokoroModelLocator.Resolve(options.TtsDirectory);
        }
        catch (FileNotFoundException ex)
        {
            // Before the microphone is opened and before anything is loaded: the user needs the files first.
            error.WriteLine($"fonia: {ex.Message}");
            return null;
        }
        catch (ArgumentException ex)
        {
            error.WriteLine($"fonia: {ex.Message}");
            return null;
        }

        var resources = new List<IDisposable>();
        var asyncResources = new List<IAsyncDisposable>();

        try
        {
            var capture = new NAudioCapture(options.MicDevice);
            resources.Add(capture);
            var pushToTalk = KeyboardPushToTalk.Create(virtualKey);
            resources.Add(pushToTalk);

            // The key that marks an exchange the controller got wrong; only when the cases are being kept.
            IPushToTalk? markKey = null;

            if (cases is not null && PushToTalkKey.TryParse(options.MarkKey, out var markVirtualKey))
            {
                markKey = KeyboardPushToTalk.Create(markVirtualKey);
                resources.Add(markKey);
            }

            var player = new NAudioPlayer(options.AudioOut);
            asyncResources.Add(player);
            var transcriber = new WhisperTranscriber(new WhisperOptions(sttModel, options.SttGpu));
            asyncResources.Add(transcriber);
            var synthesizer = new KokoroSynthesizer(new KokoroOptions(ttsFiles.Model, ttsFiles.Voices));
            asyncResources.Add(synthesizer);

            var voices = new VoiceSelector(VoiceCatalog.Default, options.TtsVoice);
            var mode = new VoiceMode(
                capture,
                pushToTalk,
                transcriber,
                synthesizer,
                player,
                new RuleBasedNlu(),
                new AtcEngine(),
                new PortuguesePhraseology(),
                voices,
                options.RadioEffect,
                options.PttKey,
                output,
                error,
                Random.Shared,
                com1,
                states,
                cases,
                markKey,
                options.MarkKey,
                aircraft);

            return new VoiceModeHandle(mode, resources, asyncResources);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            error.WriteLine($"fonia: {ex.Message}");
        }
        catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException)
        {
            error.WriteLine($"fonia: não foi possível abrir o microfone ou o alto-falante: {ex.Message}");
        }

        await new VoiceModeHandle(null, resources, asyncResources).DisposeAsync();
        return null;
    }
}

/// <summary>The voice mode together with the devices and models that belong to it, closed in the end.</summary>
internal sealed class VoiceModeHandle(IVoiceMode? mode, List<IDisposable> resources, List<IAsyncDisposable> asyncResources) : IAsyncDisposable
{
    public IVoiceMode Mode => mode ?? throw new InvalidOperationException("The voice mode was not created.");

    public async ValueTask DisposeAsync()
    {
        foreach (var resource in resources)
        {
            resource.Dispose();
        }

        foreach (var resource in asyncResources)
        {
            await resource.DisposeAsync();
        }
    }
}
