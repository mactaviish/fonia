namespace Fonia.App;

/// <summary>The options with the settings applied, or what is wrong with a setting.</summary>
internal sealed record AppliedSettings(AppOptions Options, string? Error);

/// <summary>
/// Puts the user's settings into the options of this run. What the command line said stays: <paramref name="given"/> names the options it
/// said (even with the value that is the default, which the options alone could not tell from "not said"). The values are checked the
/// way the command line checks them, and a wrong one is reported with the key it came from.
/// </summary>
internal static class SettingsApplier
{
    public static AppliedSettings Apply(AppOptions options, AppSettings settings, IReadOnlySet<string> given)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(given);

        var result = options;
        string? problem;

        if (settings.Callsign is { } callsign && !given.Contains("callsign"))
        {
            result = result with { Callsign = callsign };
        }

        if (settings.Registration is { } registration && !given.Contains("registration"))
        {
            if (StartupWizard.NormalizeCallsign(registration).Length == 0)
            {
                return Refuse("registration", "precisa de 3 a 10 letras e números, por exemplo PR-ABC.");
            }

            result = result with { Registration = registration };
        }

        if (settings.Telephony.Count > 0)
        {
            // What the command line said for a designator wins over the file.
            var companies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (designator, spoken) in settings.Telephony)
            {
                companies[designator] = spoken;
            }

            foreach (var (designator, spoken) in result.Telephony ?? new Dictionary<string, string>())
            {
                companies[designator] = spoken;
            }

            try
            {
                foreach (var (designator, spoken) in companies)
                {
                    _ = Fonia.Core.Phraseology.TelephonyTable.BuiltIn.With(designator, spoken);
                }
            }
            catch (ArgumentException ex)
            {
                return Refuse("telephony", ex.Message);
            }

            result = result with { Telephony = companies.ToDictionary(c => c.Key.ToUpperInvariant(), c => c.Value) };
        }

        if (settings.LogLevel is { } level && !given.Contains("log-level"))
        {
            result = result with { LogLevel = ArgumentParser.ParseLogLevel(level, out problem) };

            if (problem is not null)
            {
                return Refuse("logLevel", problem);
            }
        }

        if (settings.Weather.Metar == false && !given.Contains("no-metar"))
        {
            result = result with { NoMetar = true };
        }

        if (settings.Cases.Save == false && !given.Contains("no-cases"))
        {
            result = result with { NoCases = true };
        }

        if (settings.Cases.SaveAudio == true && !given.Contains("save-audio"))
        {
            result = result with { SaveAudio = true };
        }

        var voice = settings.Voice;

        if (voice.MarkKey is { } markKey && !given.Contains("mark-key"))
        {
            result = result with { MarkKey = ArgumentParser.ParsePttKey(markKey, out problem, "--mark-key") ?? result.MarkKey };

            if (problem is not null)
            {
                return Refuse("voice.markKey", problem);
            }
        }

        if (voice.PttKey is { } key && !given.Contains("ptt-key"))
        {
            result = result with { PttKey = ArgumentParser.ParsePttKey(key, out problem) ?? result.PttKey };

            if (problem is not null)
            {
                return Refuse("voice.pttKey", problem);
            }
        }

        if (voice.MicDevice is { } mic && !given.Contains("mic-device"))
        {
            result = result with { MicDevice = mic };
        }

        if (voice.AudioOut is { } audio && !given.Contains("audio-out"))
        {
            result = result with { AudioOut = audio };
        }

        if (voice.TtsVoice is { } ttsVoice && !given.Contains("tts-voice"))
        {
            var parsed = ArgumentParser.ParseTtsVoice(ttsVoice, out problem);

            if (problem is not null)
            {
                return Refuse("voice.ttsVoice", problem);
            }

            result = result with { TtsVoice = string.IsNullOrEmpty(parsed) ? null : parsed };
        }

        if (voice.RadioEffect is { } effect && !given.Contains("radio-effect"))
        {
            var parsed = ArgumentParser.ParseRadioEffect(effect, out problem);

            if (problem is not null)
            {
                return Refuse("voice.radioEffect", problem);
            }

            result = result with { RadioEffect = parsed!.Value };
        }

        if (voice.SttGpu is { } gpu && !given.Contains("stt-gpu"))
        {
            result = result with { SttGpu = gpu };
        }

        if (voice.SttModel is { } model && !given.Contains("stt-model"))
        {
            result = result with { SttModel = model };
        }

        if (voice.ModelsDir is { } directory)
        {
            result = result with
            {
                SttDirectory = given.Contains("stt-dir") ? result.SttDirectory : directory,
                TtsDirectory = given.Contains("tts-dir") ? result.TtsDirectory : directory,
                ModelsDir = given.Contains("models-dir") ? result.ModelsDir : directory,
            };
        }

        return new AppliedSettings(result, null);
    }

    private static AppliedSettings Refuse(string key, string problem) =>
        new(new AppOptions(), $"nas configurações ({SettingsLoader.FileName} ou FONIA_*), '{key}': {problem}");
}
