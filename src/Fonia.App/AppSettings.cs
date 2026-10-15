namespace Fonia.App;

/// <summary>
/// The preferences a user keeps in <c>appsettings.json</c>: what they would otherwise type on every start. Not modes: what the app does
/// still comes from the command line. Every key is optional; the command line wins over all of them. Names are camelCase in the file.
/// </summary>
internal sealed class AppSettings
{
    /// <summary>The airport the app starts at when it is opened without arguments (the question shows it as the answer to accept).</summary>
    public string? Airport { get; set; }

    /// <summary>The aircraft's callsign when it is not read from the simulator (<c>--callsign</c>).</summary>
    public string? Callsign { get; set; }

    /// <summary>trace, debug, information, warning, error, critical or none (<c>--log-level</c>).</summary>
    public string? LogLevel { get; set; }

    public VoiceSettings Voice { get; } = new();

    public WeatherSettings Weather { get; } = new();

    internal sealed class VoiceSettings
    {
        /// <summary>The push-to-talk key (<c>--ptt-key</c>), such as "RControl" or "F8".</summary>
        public string? PttKey { get; set; }

        /// <summary>The microphone, as numbered by <c>--mic-list</c> (<c>--mic-device</c>).</summary>
        public int? MicDevice { get; set; }

        /// <summary>The loudspeaker, as numbered by <c>--mic-list</c> (<c>--audio-out</c>).</summary>
        public int? AudioOut { get; set; }

        /// <summary>pf_dora, pm_alex or "aleatoria" (<c>--tts-voice</c>).</summary>
        public string? TtsVoice { get; set; }

        /// <summary>desligado, leve, media or forte (<c>--radio-effect</c>).</summary>
        public string? RadioEffect { get; set; }

        /// <summary>Recognize on the GPU (<c>--stt-gpu</c>): 0.25 s instead of 2 s, in exchange for using the simulator's card for an instant.</summary>
        public bool? SttGpu { get; set; }

        /// <summary>The recognizer's model file (<c>--stt-model</c>).</summary>
        public string? SttModel { get; set; }

        /// <summary>Where the voice models are (<c>--stt-dir</c>, <c>--tts-dir</c> and <c>--models-dir</c> together).</summary>
        public string? ModelsDir { get; set; }
    }

    internal sealed class WeatherSettings
    {
        /// <summary>false turns the METAR off (<c>--no-metar</c>).</summary>
        public bool? Metar { get; set; }
    }
}
