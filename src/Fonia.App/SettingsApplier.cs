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

        var voice = settings.Voice;

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
