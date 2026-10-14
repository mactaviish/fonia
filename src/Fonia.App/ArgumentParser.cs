using System.Globalization;
using Fonia.Audio;
using Fonia.Core.Speech;
using Microsoft.Extensions.Logging;

namespace Fonia.App;

internal enum SimSourceKind
{
    Msfs,
    Mock,
    Replay,
}

/// <summary>What the command line asks for.</summary>
/// <param name="Speed">Replay speed: 1 is real time, <see cref="double.PositiveInfinity"/> is "as fast as possible".</param>
internal sealed record AppOptions(
    SimSourceKind Source = SimSourceKind.Msfs,
    string? ReplayPath = null,
    double Speed = 1.0,
    string? RecordPath = null,
    TimeSpan? Duration = null,
    LogLevel LogLevel = LogLevel.Warning,
    string? AtcAirport = null,
    string? Runway = null,
    string? Callsign = null,
    string? MicTestPath = null,
    string PttKey = PushToTalkKey.DefaultName,
    int MicDevice = -1,
    bool ListMics = false,
    string? TranscribePath = null,
    string? SttDirectory = null,
    string? SttModel = null,
    bool SttGpu = false,
    string? SpeakText = null,
    string SpeakOut = "fonia-speak.wav",
    string? TtsVoice = null,
    string? TtsDirectory = null,
    RadioIntensity RadioEffect = Core.Speech.RadioEffect.DefaultIntensity);

/// <summary>The outcome of reading the command line: options, a message about what is wrong, or a request for help.</summary>
internal sealed record ParsedArguments(AppOptions? Options, string? Error, bool ShowHelp);

/// <summary>Reads the command line by hand: the options are few and a parsing library would be the heaviest part of the app.</summary>
internal static class ArgumentParser
{
    public const string HelpText = """
        Fonia: estado da aeronave em tempo real, vindo do simulador, e ATC por texto.

        Uso: fonia [opções]
             fonia --atc <ICAO> [--runway <pista>] [--callsign <indicativo>]
             fonia --mic-test <arquivo.wav> [--mic-device <n>] [--ptt-key <tecla>]
             fonia --mic-list
             fonia --transcribe <arquivo.wav> [--callsign <indicativo>] [--stt-dir <pasta>] [--stt-gpu]
             fonia --speak "<texto>" [--speak-out <arquivo.wav>] [--tts-voice <voz>] [--radio-effect <nível>]

          --atc <ICAO>           Modo ATC por texto: você digita a fala do piloto e o controlador do
                                 aeroporto responde (ex.: --atc SBCT). Escreva "sair" para encerrar.
          --runway <pista>       Pista em uso (ex.: 15, 33L). Padrão: a mais longa do aeroporto.
          --callsign <indicativo>  Indicativo da aeronave (ex.: PR-ABC). Sem ele, vem da matrícula
                                 da aeronave no simulador (--sim). Com ele, não usa simulador.

          --mic-test <arquivo.wav>  Testa o microfone e o push-to-talk: segure a tecla, fale, solte, e a
                                 fala é salva no arquivo. Só no Windows.
          --mic-device <n>       Microfone do teste (número de --mic-list). Padrão: o do Windows.
          --ptt-key <tecla>      Tecla do push-to-talk. Padrão: RControl. Exemplos: RControl, LControl, RShift,
                                 RAlt, CapsLock, Space, ScrollLock, Insert, F8, Mouse4, Mouse5, letras, números e F1 a F24.
          --mic-list             Lista os microfones e seus números.

          --transcribe <arquivo.wav>  Transcreve uma fala gravada (WAV de 16 kHz, mono, 16 bits, como o do
                                 --mic-test), localmente e sem internet, e mostra o que o NLU entendeu.
                                 O indicativo (--callsign) ajuda o reconhecimento.
          --stt-dir <pasta>      Pasta do modelo de voz. Padrão: %LOCALAPPDATA%\Fonia\models. O modelo
                                 (ggml-small.bin, 466 MB) é baixado por você; a mensagem de erro diz de onde.
          --stt-model <arquivo>  Nome do arquivo do modelo. Padrão: ggml-small.bin.
          --stt-gpu              Usa a GPU (Vulkan) no reconhecimento: ~0,25 s por frase em vez de ~2 s,
                                 em troca de usar a placa do simulador por instantes. Padrão: só CPU.

          --speak "<texto>"      Fala o texto com a voz do controlador (local, sem internet) e salva em um
                                 WAV: serve para ouvir como o controlador soa. Só o texto, sem o motor ATC.
          --speak-out <arquivo>  Arquivo de saída. Padrão: fonia-speak.wav.
          --tts-voice <voz>      pf_dora, pm_alex ou aleatoria (padrão): sorteia uma das vozes aprovadas.
          --radio-effect <nível> Efeito de rádio: desligado, leve, media (padrão) ou forte.
          --tts-dir <pasta>      Pasta do modelo de voz. Padrão: %LOCALAPPDATA%\Fonia\models. Precisa de
                                 kokoro-v1.0.onnx (325 MB) e voices-v1.0.bin (28 MB), baixados por você; a
                                 mensagem de erro diz de onde.

          --sim msfs|mock        Fonte dos dados. msfs (padrão) lê o Microsoft Flight Simulator 2020;
                                 mock mostra um taxi simulado, para testar sem simulador.
          --replay <arquivo>     Reproduz uma gravação (.jsonl) em vez de ler um simulador.
          --speed <n|max>        Velocidade da reprodução: 1 = tempo real (padrão), 2 = o dobro,
                                 max = sem esperar (a tela mostra só alguns snapshots, mas
                                 o arquivo gravado com --record não é afetado). Só vale com --replay.
          --record <arquivo>     Grava a sessão em um arquivo .jsonl, além de mostrá-la.
          --duration <segundos>  Encerra sozinho depois desse tempo.
          --log-level <nível>    trace, debug, information, warning (padrão), error, critical ou none.
                                 Os logs vão para a saída de erro.
          --help, -h             Mostra esta ajuda.

        Ctrl+C encerra com limpeza (a gravação fica válida).
        Códigos de saída: 0 normal, 1 erro durante a execução, 2 uso incorreto.
        """;

    private static readonly string[] LogLevelNames = ["trace", "debug", "information", "warning", "error", "critical", "none"];

    public static ParsedArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        SimSourceKind? source = null;
        string? replay = null;
        string? record = null;
        string? atc = null;
        string? runway = null;
        string? callsign = null;
        string? micTest = null;
        string? pttKey = null;
        int? micDevice = null;
        var listMics = false;
        string? transcribe = null;
        string? sttDir = null;
        string? sttModel = null;
        var sttGpu = false;
        string? speak = null;
        string? speakOut = null;
        string? ttsVoice = null;
        string? ttsDir = null;
        RadioIntensity? radioEffect = null;
        double? speed = null;
        TimeSpan? duration = null;
        var logLevel = LogLevel.Warning;

        for (var i = 0; i < args.Length; i++)
        {
            var (name, inlineValue) = Split(args[i]);

            if (name is "--help" or "-h" or "/?")
            {
                return new ParsedArguments(null, null, ShowHelp: true);
            }

            if (name == "--mic-list")
            {
                listMics = true;
                continue;
            }

            if (name == "--stt-gpu")
            {
                sttGpu = true;
                continue;
            }

            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                return Failure($"argumento inesperado: '{args[i]}'. Use --help para ver as opções.");
            }

            if (name is not ("--sim" or "--replay" or "--speed" or "--record" or "--duration" or "--log-level" or "--atc" or "--runway" or "--callsign" or "--mic-test" or "--mic-device" or "--ptt-key" or "--transcribe" or "--stt-dir" or "--stt-model" or "--speak" or "--speak-out" or "--tts-voice" or "--tts-dir" or "--radio-effect"))
            {
                return Failure($"opção desconhecida: '{name}'. Use --help para ver as opções.");
            }

            var value = inlineValue ?? (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null);

            if (value is null)
            {
                return Failure($"{name} precisa de um valor.");
            }

            string? problem = null;

            switch (name)
            {
                case "--sim":
                    source = ParseSource(value, out problem);
                    break;
                case "--replay":
                    replay = value;
                    break;
                case "--record":
                    record = value;
                    break;
                case "--atc":
                    atc = value.Trim().ToUpperInvariant();
                    break;
                case "--runway":
                    runway = value;
                    break;
                case "--callsign":
                    callsign = value;
                    break;
                case "--mic-test":
                    micTest = value;
                    break;
                case "--mic-device":
                    micDevice = ParseMicDevice(value, out problem);
                    break;
                case "--ptt-key":
                    pttKey = ParsePttKey(value, out problem);
                    break;
                case "--transcribe":
                    transcribe = value;
                    break;
                case "--stt-dir":
                    sttDir = value;
                    break;
                case "--stt-model":
                    sttModel = value;
                    break;
                case "--speak":
                    speak = value;
                    break;
                case "--speak-out":
                    speakOut = value;
                    break;
                case "--tts-voice":
                    ttsVoice = ParseTtsVoice(value, out problem);
                    break;
                case "--tts-dir":
                    ttsDir = value;
                    break;
                case "--radio-effect":
                    radioEffect = ParseRadioEffect(value, out problem);
                    break;
                case "--speed":
                    speed = ParseSpeed(value, out problem);
                    break;
                case "--duration":
                    duration = ParseDuration(value, out problem);
                    break;
                case "--log-level":
                    logLevel = ParseLogLevel(value, out problem);
                    break;
            }

            if (problem is not null)
            {
                return Failure(problem);
            }
        }

        return Validate(source, replay, record, speed, duration, logLevel, atc, runway, callsign, new MicRaw(micTest, micDevice, pttKey, listMics), new SttRaw(transcribe, sttDir, sttModel, sttGpu), new TtsRaw(speak, speakOut, ttsVoice, ttsDir, radioEffect));
    }

    private sealed record MicRaw(string? TestPath, int? Device, string? Key, bool List);

    // Voice: "" asked for the random one by name, null not given.
    private sealed record TtsRaw(string? Text, string? Out, string? Voice, string? Dir, RadioIntensity? Effect)
    {
        public bool Any => Text is not null || Out is not null || Voice is not null || Dir is not null || Effect is not null;
    }

    private sealed record SttRaw(string? TranscribePath, string? Directory, string? Model, bool Gpu)
    {
        public bool Any => TranscribePath is not null || Directory is not null || Model is not null || Gpu;
    }

    private static ParsedArguments Validate(
        SimSourceKind? source, string? replay, string? record, double? speed, TimeSpan? duration, LogLevel logLevel, string? atc, string? runway, string? callsign, MicRaw mic, SttRaw stt, TtsRaw tts)
    {
        var others = source is not null || replay is not null || record is not null || speed is not null || duration is not null
            || atc is not null || runway is not null || callsign is not null;

        if (mic.List)
        {
            return others || stt.Any || tts.Any || mic.TestPath is not null || mic.Device is not null || mic.Key is not null
                ? Failure("--mic-list não combina com outras opções: use só --mic-list.")
                : new ParsedArguments(new AppOptions(LogLevel: logLevel, ListMics: true), null, ShowHelp: false);
        }

        if (tts.Text is not null)
        {
            var mixed = source is not null || replay is not null || record is not null || speed is not null || duration is not null
                || atc is not null || runway is not null || callsign is not null
                || mic.TestPath is not null || mic.Device is not null || mic.Key is not null || stt.Any;

            return mixed
                ? Failure("--speak não combina com os outros modos (--sim, --replay, --record, --speed, --duration, --atc, --callsign, microfone, --transcribe).")
                : new ParsedArguments(
                    new AppOptions(
                        LogLevel: logLevel,
                        SpeakText: tts.Text,
                        SpeakOut: tts.Out ?? "fonia-speak.wav",
                        TtsVoice: string.IsNullOrEmpty(tts.Voice) ? null : tts.Voice,
                        TtsDirectory: tts.Dir,
                        RadioEffect: tts.Effect ?? Core.Speech.RadioEffect.DefaultIntensity),
                    null,
                    ShowHelp: false);
        }

        if (tts.Any)
        {
            return Failure("--speak-out, --tts-voice, --tts-dir e --radio-effect só valem ao falar: use também --speak \"<texto>\".");
        }

        if (stt.TranscribePath is not null)
        {
            var mixed = source is not null || replay is not null || record is not null || speed is not null || duration is not null
                || atc is not null || runway is not null
                || mic.TestPath is not null || mic.Device is not null || mic.Key is not null;

            return mixed
                ? Failure("--transcribe não combina com --sim, --replay, --record, --speed, --duration, --atc, --runway nem as opções do microfone.")
                : new ParsedArguments(
                    new AppOptions(LogLevel: logLevel, Callsign: callsign, TranscribePath: stt.TranscribePath, SttDirectory: stt.Directory, SttModel: stt.Model, SttGpu: stt.Gpu),
                    null,
                    ShowHelp: false);
        }

        if (stt.Any)
        {
            return Failure("--stt-dir, --stt-model e --stt-gpu só valem ao transcrever: use também --transcribe <arquivo.wav>.");
        }

        if (mic.TestPath is null && (mic.Device is not null || mic.Key is not null))
        {
            return Failure("--mic-device e --ptt-key só valem no teste do microfone: use também --mic-test <arquivo.wav>.");
        }

        if (mic.TestPath is not null)
        {
            return others
                ? Failure("--mic-test não combina com --sim, --replay, --record, --speed, --duration, --atc, --runway nem --callsign.")
                : new ParsedArguments(
                    new AppOptions(LogLevel: logLevel, MicTestPath: mic.TestPath, PttKey: mic.Key ?? PushToTalkKey.DefaultName, MicDevice: mic.Device ?? -1),
                    null,
                    ShowHelp: false);
        }

        if (atc is null && (runway is not null || callsign is not null))
        {
            return Failure("--runway e --callsign só valem no modo ATC: use também --atc <ICAO>.");
        }

        if (atc is not null)
        {
            return ValidateAtc(source, replay, record, speed, duration, logLevel, atc, runway, callsign);
        }

        if (replay is not null && source is SimSourceKind.Msfs or SimSourceKind.Mock)
        {
            return Failure("--replay não pode ser usado com --sim msfs ou --sim mock; use só --replay <arquivo>.");
        }

        if (replay is null && source == SimSourceKind.Replay)
        {
            return Failure("--sim replay precisa de --replay <arquivo>.");
        }

        var effective = replay is not null ? SimSourceKind.Replay : source ?? SimSourceKind.Msfs;

        if (speed is not null && effective != SimSourceKind.Replay)
        {
            return Failure("--speed só vale para a reprodução: use também --replay <arquivo>.");
        }

        return new ParsedArguments(new AppOptions(effective, replay, speed ?? 1.0, record, duration, logLevel), null, ShowHelp: false);
    }

    private static ParsedArguments ValidateAtc(
        SimSourceKind? source, string? replay, string? record, double? speed, TimeSpan? duration, LogLevel logLevel, string atc, string? runway, string? callsign)
    {
        if (replay is not null || record is not null || speed is not null || duration is not null || source == SimSourceKind.Replay)
        {
            return Failure("--atc não combina com --replay, --record, --speed nem --duration.");
        }

        if (callsign is not null && source is not null)
        {
            return Failure("--callsign dispensa o simulador: remova --sim, ou remova --callsign para usar a matrícula do simulador.");
        }

        return new ParsedArguments(new AppOptions(source ?? SimSourceKind.Msfs, LogLevel: logLevel, AtcAirport: atc, Runway: runway, Callsign: callsign), null, ShowHelp: false);
    }

    private static (string Name, string? Value) Split(string argument)
    {
        var equals = argument.IndexOf('=', StringComparison.Ordinal);
        return argument.StartsWith("--", StringComparison.Ordinal) && equals > 0
            ? (argument[..equals], argument[(equals + 1)..])
            : (argument, null);
    }

    private static SimSourceKind? ParseSource(string value, out string? problem)
    {
        problem = null;

        switch (value.ToLowerInvariant())
        {
            case "msfs":
                return SimSourceKind.Msfs;
            case "mock":
                return SimSourceKind.Mock;
            case "replay":
                return SimSourceKind.Replay;
            default:
                problem = $"--sim '{value}' não existe. Use msfs ou mock (ou --replay <arquivo>).";
                return null;
        }
    }

    private static double? ParseSpeed(string value, out string? problem)
    {
        problem = null;

        if (value.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return double.PositiveInfinity;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) && double.IsFinite(speed) && speed > 0)
        {
            return speed;
        }

        problem = $"--speed '{value}' é inválido: use um número maior que zero (ex.: 1, 2, 0.5) ou max.";
        return null;
    }

    private static TimeSpan? ParseDuration(string value, out string? problem)
    {
        problem = null;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && double.IsFinite(seconds) && seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        problem = $"--duration '{value}' é inválido: use um número de segundos maior que zero.";
        return null;
    }

    private static string? ParseTtsVoice(string value, out string? problem)
    {
        problem = null;
        var name = value.Trim().ToLowerInvariant().Replace("ó", "o", StringComparison.Ordinal);

        if (name is "aleatoria" or "random")
        {
            return string.Empty;
        }

        if (VoiceCatalog.Find(value) is { } voice)
        {
            return voice.Id;
        }

        problem = $"--tts-voice '{value}' não existe. Use: {string.Join(", ", VoiceCatalog.Default.Select(v => v.Id))} ou aleatoria.";
        return null;
    }

    private static RadioIntensity? ParseRadioEffect(string value, out string? problem)
    {
        problem = null;

        if (Core.Speech.RadioEffect.TryParse(value, out var intensity))
        {
            return intensity;
        }

        problem = $"--radio-effect '{value}' não existe. Use: desligado, leve, média ou forte.";
        return null;
    }

    private static int? ParseMicDevice(string value, out string? problem)
    {
        problem = null;

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var device))
        {
            return device;
        }

        problem = $"--mic-device '{value}' é inválido: use o número mostrado por --mic-list (0, 1, 2...).";
        return null;
    }

    private static string? ParsePttKey(string value, out string? problem)
    {
        problem = null;

        if (PushToTalkKey.TryParse(value, out _))
        {
            return value;
        }

        problem = $"--ptt-key '{value}' não existe. Exemplos: {string.Join(", ", PushToTalkKey.ExampleNames)} (também letras, números e F1 a F24).";
        return null;
    }

    private static LogLevel ParseLogLevel(string value, out string? problem)
    {
        problem = null;
        var index = Array.IndexOf(LogLevelNames, value.ToLowerInvariant());

        if (index >= 0)
        {
            return (LogLevel)index;
        }

        problem = $"--log-level '{value}' não existe. Use: {string.Join(", ", LogLevelNames)}.";
        return LogLevel.Warning;
    }

    private static ParsedArguments Failure(string message) => new(null, message, ShowHelp: false);
}
