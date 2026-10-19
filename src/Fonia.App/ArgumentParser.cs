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
    RadioIntensity RadioEffect = Core.Speech.RadioEffect.DefaultIntensity,
    bool Voice = false,
    int AudioOut = -1,
    bool DownloadModels = false,
    string? ModelsDir = null,
    Fonia.Core.Sim.Frequency? Com1 = null,
    bool NoMetar = false,
    bool NoMap = false,
    bool ShowConfig = false,
    bool InitConfig = false,
    string? OnlyModels = null,
    bool NoCases = false,
    bool SaveAudio = false,
    string MarkKey = PushToTalkKey.DefaultMarkName,
    bool ShowCases = false,
    string? DumpLayoutAirport = null);

/// <summary>The outcome of reading the command line: options, a message about what is wrong, or a request for help.</summary>
internal sealed record ParsedArguments(AppOptions? Options, string? Error, bool ShowHelp, bool ShowVersion = false);

/// <summary>Reads the command line by hand: the options are few and a parsing library would be the heaviest part of the app.</summary>
internal static class ArgumentParser
{
    public const string HelpText = """
        Fonia: estado da aeronave em tempo real, vindo do simulador, e ATC por texto.

        Uso: fonia [opções]
             fonia                  (sem argumentos, num terminal: pega o indicativo e o aeroporto do simulador, ou pergunta, e conversa por voz)
             fonia --atc <ICAO> [--runway <pista>] [--callsign <indicativo>] [--com1 <MHz>] [--no-metar] [--no-map]
             fonia --config | --init-config | --cases
             fonia --dump-layout <ICAO> [--log-level <nível>]
             fonia --mic-test <arquivo.wav> [--mic-device <n>] [--ptt-key <tecla>]
             fonia --mic-list
             fonia --transcribe <arquivo.wav> [--callsign <indicativo>] [--stt-dir <pasta>] [--stt-gpu]
             fonia --atc <ICAO> --voice [--callsign <indicativo>] [--ptt-key <tecla>] [--stt-gpu] [--tts-voice <voz>]
             fonia --download-models [--only stt|tts] [--models-dir <pasta>]
             fonia --speak "<texto>" [--speak-out <arquivo.wav>] [--tts-voice <voz>] [--radio-effect <nível>]

          --atc <ICAO>           Modo ATC por texto: você digita a fala do piloto e o controlador do
                                 aeroporto responde (ex.: --atc SBCT). Escreva "sair" para encerrar.
          --runway <pista>       Pista em uso (ex.: 15, 33L). Padrão: a mais longa do aeroporto.
          --config               Mostra de onde vêm as configurações e o que está configurado.
          --init-config          Cria o seu arquivo de configuração (%APPDATA%\Fonia\appsettings.json) com todas as opções
                                 comentadas, para você editar. Suas preferências (tecla do PTT, microfone, voz, GPU...) ficam
                                 lá e a linha de comando sempre vale mais. Variáveis FONIA_<chave> também valem.
          --no-cases             Não guarda os casos em que o controlador não entendeu. Por padrão o Fonia guarda, só no seu
                                 computador (%LOCALAPPDATA%\Fonia\cases, por até 30 dias e 200 MB), o texto, o contexto e o que ele
                                 respondeu, para melhorar o programa. Nada é enviado. Apague a pasta quando quiser.
          --save-audio           Com --voice: guarda também o som da sua fala em cada caso (sua voz é dado pessoal: desligado por padrão).
          --mark-key <tecla>     Com --voice: tecla que marca a última troca como "o controlador entendeu errado" (padrão: ScrollLock).
                                 No modo texto, "/errado" faz o mesmo.
          --cases                Mostra onde estão os casos guardados e quantos há por sessão.
          --dump-layout <ICAO>   Pergunta ao MSFS 2020 (aberto, com um voo carregado) o que ele sabe do aeroporto: pistas,
                                 frequências de rádio, pontos e caminhos de táxi, nomes de taxiway e estacionamentos. Só lê.
                                 Grava o que veio, sem interpretar, em %LOCALAPPDATA%\Fonia\layouts\raw\<ICAO>.raw.json e
                                 mostra um resumo e a comparação das frequências com as do OurAirports. Serve para ver o que o
                                 simulador entrega antes de usar o mapa do aeródromo; nada é enviado.
          --no-metar             Não busca o METAR na internet (aviationweather.gov, só o código do aeroporto vai pela rede).
                                 Com o METAR o controlador diz o vento e o ajuste de altímetro e a pista em uso é a que o
                                 vento favorece (--runway manda); sem ele, ou sem rede, essas partes ficam de fora. O último
                                 METAR fica guardado em %LOCALAPPDATA%\Fonia\cache e vale por até 3 horas sem rede.
          --no-map               Não usa o mapa do aeródromo nem as frequências do simulador, só os dados do OurAirports. Por padrão,
                                 com o MSFS aberto o Fonia pede ao simulador o mapa (taxiways) e as frequências do aeroporto e os guarda
                                 em %LOCALAPPDATA%\Fonia\layouts; sem o simulador usa o que ficou guardado, e sem nenhum dos dois
                                 funciona como sempre, com os dados do OurAirports.
          --com1 <MHz>           Frequência do rádio (COM1), por exemplo 118.55: quem responde é a unidade do aeroporto
                                 que usa essa frequência (Solo, Torre ou Rádio), e em outra ninguém responde. Com o
                                 simulador (--sim) a frequência vem do COM1 dele, ao vivo. No modo texto, "/com1 118.55"
                                 muda durante a sessão. Sem rádio, responde a unidade padrão do aeroporto.
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

          --voice                Com --atc: conversa por voz. Segure o push-to-talk, fale, solte, e o controlador
                                 responde em voz alta, tudo local. O texto do que ele entendeu e da resposta aparece
                                 na tela, com o tempo de cada etapa. Enquanto o controlador fala, o PTT é ignorado
                                 (canal ocupado). Usa as opções --ptt-key, --mic-device, --audio-out, --stt-*,
                                 --tts-* e --radio-effect. Só no Windows.
          --audio-out <n>        Alto-falante da resposta (número de --mic-list). Padrão: o do Windows.

          --download-models      Baixa os modelos de voz que faltam (reconhecimento e voz do controlador, ~820 MB no total)
                                 dos donos, confere o SHA-256 e guarda em %LOCALAPPDATA%\Fonia\models. Pode ser
                                 interrompido: continua de onde parou. Os modos de voz também oferecem isso quando
                                 falta modelo.
          --only <stt|tts|all>   Com --download-models: baixa só o reconhecimento de voz (stt), só a voz do controlador (tts) ou tudo (padrão).
          --models-dir <pasta>   Outra pasta para o --download-models (use a mesma em --stt-dir e --tts-dir).

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
          --version              Mostra a versão do programa.
          --help, -h             Mostra esta ajuda.

        Ctrl+C encerra com limpeza (a gravação fica válida).
        Códigos de saída: 0 normal, 1 erro durante a execução, 2 uso incorreto.
        """;

    private static readonly string[] LogLevelNames = ["trace", "debug", "information", "warning", "error", "critical", "none"];

    /// <summary>The names (without the dashes) of the options the command line has, so that the settings do not override them.</summary>
    public static IReadOnlySet<string> OptionsGiven(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args
            .Where(a => a.StartsWith("--", StringComparison.Ordinal))
            .Select(a => Split(a).Name[2..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static ParsedArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        SimSourceKind? source = null;
        string? replay = null;
        string? record = null;
        string? atc = null;
        string? runway = null;
        string? callsign = null;
        Fonia.Core.Sim.Frequency? com1 = null;
        string? micTest = null;
        string? pttKey = null;
        int? micDevice = null;
        var listMics = false;
        string? transcribe = null;
        string? sttDir = null;
        string? sttModel = null;
        var sttGpu = false;
        var noMetar = false;
        var noMap = false;
        var noCases = false;
        var saveAudio = false;
        string? markKey = null;
        string? only = null;
        string? speak = null;
        string? speakOut = null;
        string? ttsVoice = null;
        string? ttsDir = null;
        RadioIntensity? radioEffect = null;
        var voice = false;
        int? audioOut = null;
        var downloadModels = false;
        string? modelsDir = null;
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

            if (name == "--version")
            {
                return new ParsedArguments(null, null, ShowHelp: false, ShowVersion: true);
            }

            if (name == "--mic-list")
            {
                listMics = true;
                continue;
            }

            if (name == "--download-models")
            {
                downloadModels = true;
                continue;
            }

            if (name == "--voice")
            {
                voice = true;
                continue;
            }

            if (name == "--stt-gpu")
            {
                sttGpu = true;
                continue;
            }

            if (name == "--no-metar")
            {
                noMetar = true;
                continue;
            }

            if (name == "--no-map")
            {
                noMap = true;
                continue;
            }

            if (name is "--config" or "--init-config" or "--cases")
            {
                if (args.Length > 1)
                {
                    return Failure($"{name} não combina com outras opções: use só {name}.");
                }

                return new ParsedArguments(new AppOptions(ShowConfig: name == "--config", InitConfig: name == "--init-config", ShowCases: name == "--cases"), null, ShowHelp: false);
            }

            if (name == "--dump-layout")
            {
                return ParseDumpLayout(args);
            }

            if (name == "--no-cases")
            {
                noCases = true;
                continue;
            }

            if (name == "--save-audio")
            {
                saveAudio = true;
                continue;
            }

            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                return Failure($"argumento inesperado: '{args[i]}'. Use --help para ver as opções.");
            }

            if (name is not ("--sim" or "--replay" or "--speed" or "--record" or "--duration" or "--log-level" or "--atc" or "--runway" or "--callsign" or "--mic-test" or "--mic-device" or "--ptt-key" or "--transcribe" or "--stt-dir" or "--stt-model" or "--speak" or "--speak-out" or "--tts-voice" or "--tts-dir" or "--radio-effect" or "--audio-out" or "--models-dir" or "--com1" or "--only" or "--mark-key"))
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
                case "--com1":
                    com1 = ArgumentParserFrequency(value, out problem);
                    break;
                case "--models-dir":
                    modelsDir = value;
                    break;
                case "--only":
                    only = value.ToLowerInvariant();
                    break;
                case "--mark-key":
                    markKey = ParsePttKey(value, out problem, "--mark-key");
                    break;
                case "--audio-out":
                    audioOut = ParseDevice("--audio-out", value, out problem);
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

        var parsed = Validate(source, replay, record, speed, duration, logLevel, atc, runway, callsign, new MicRaw(micTest, micDevice, pttKey, listMics), new SttRaw(transcribe, sttDir, sttModel, sttGpu), new TtsRaw(speak, speakOut, ttsVoice, ttsDir, radioEffect), new VoiceRaw(voice, audioOut), new DownloadRaw(downloadModels, modelsDir), com1);

        if (parsed.Options is null)
        {
            return parsed;
        }

        var result = parsed.Options;

        if (only is not null)
        {
            if (only is not ("stt" or "tts" or "all"))
            {
                return Failure($"--only '{only}' não existe. Use stt (reconhecimento de voz), tts (voz do controlador) ou all.");
            }

            if (!result.DownloadModels)
            {
                return Failure("--only só vale com --download-models.");
            }

            result = result with { OnlyModels = only };
        }

        if (noMetar)
        {
            if (result.AtcAirport is null)
            {
                return Failure("--no-metar só vale no modo ATC: use também --atc <ICAO>.");
            }

            result = result with { NoMetar = true };
        }

        if (noMap)
        {
            if (result.AtcAirport is null)
            {
                return Failure("--no-map só vale no modo ATC: use também --atc <ICAO>.");
            }

            result = result with { NoMap = true };
        }

        if (noCases || saveAudio || markKey is not null)
        {
            if (result.AtcAirport is null)
            {
                return Failure("--no-cases, --save-audio e --mark-key só valem no modo ATC: use também --atc <ICAO>.");
            }

            if ((saveAudio || markKey is not null) && !result.Voice)
            {
                return Failure("--save-audio e --mark-key só valem no modo de voz: use também --voice.");
            }

            if (noCases && saveAudio)
            {
                return Failure("--no-cases e --save-audio não combinam: sem guardar os casos não há o que gravar.");
            }

            result = result with { NoCases = noCases, SaveAudio = saveAudio, MarkKey = markKey ?? result.MarkKey };

            if (string.Equals(result.MarkKey, result.PttKey, StringComparison.OrdinalIgnoreCase))
            {
                return Failure($"--mark-key e --ptt-key não podem ser a mesma tecla ({result.PttKey}).");
            }
        }

        return result == parsed.Options ? parsed : new ParsedArguments(result, null, ShowHelp: false);
    }

    private sealed record MicRaw(string? TestPath, int? Device, string? Key, bool List);

    // Voice: "" asked for the random one by name, null not given.
    private sealed record TtsRaw(string? Text, string? Out, string? Voice, string? Dir, RadioIntensity? Effect)
    {
        public bool Any => Text is not null || Out is not null || Voice is not null || Dir is not null || Effect is not null;
    }

    private sealed record VoiceRaw(bool On, int? AudioOut);

    private sealed record DownloadRaw(bool On, string? Dir);

    private sealed record SttRaw(string? TranscribePath, string? Directory, string? Model, bool Gpu)
    {
        public bool Any => TranscribePath is not null || Directory is not null || Model is not null || Gpu;
    }

    private static ParsedArguments Validate(
        SimSourceKind? source, string? replay, string? record, double? speed, TimeSpan? duration, LogLevel logLevel, string? atc, string? runway, string? callsign, MicRaw mic, SttRaw stt, TtsRaw tts, VoiceRaw voice, DownloadRaw download, Fonia.Core.Sim.Frequency? com1)
    {
        var others = source is not null || replay is not null || record is not null || speed is not null || duration is not null
            || atc is not null || runway is not null || callsign is not null || com1 is not null;

        if (download.On || download.Dir is not null)
        {
            if (!download.On)
            {
                return Failure("--models-dir só vale com --download-models.");
            }

            var mixed = others || mic.List || mic.TestPath is not null || mic.Device is not null || mic.Key is not null
                || stt.Any || tts.Any || voice.On || voice.AudioOut is not null;

            return mixed
                ? Failure("--download-models não combina com outras opções: use só --download-models (e --only, --models-dir).")
                : new ParsedArguments(new AppOptions(LogLevel: logLevel, DownloadModels: true, ModelsDir: download.Dir), null, ShowHelp: false);
        }

        if (mic.List)
        {
            return others || stt.Any || tts.Any || voice.On || voice.AudioOut is not null || mic.TestPath is not null || mic.Device is not null || mic.Key is not null
                ? Failure("--mic-list não combina com outras opções: use só --mic-list.")
                : new ParsedArguments(new AppOptions(LogLevel: logLevel, ListMics: true), null, ShowHelp: false);
        }

        if (voice.On)
        {
            return ValidateVoice(source, replay, record, speed, duration, logLevel, atc, runway, callsign, com1, mic, stt, tts, voice);
        }

        if (voice.AudioOut is not null)
        {
            return Failure("--audio-out só vale no modo de voz: use também --atc <ICAO> --voice.");
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

        if (atc is null && (runway is not null || callsign is not null || com1 is not null))
        {
            return Failure("--runway, --callsign e --com1 só valem no modo ATC: use também --atc <ICAO>.");
        }

        if (atc is not null)
        {
            return ValidateAtc(source, replay, record, speed, duration, logLevel, atc, runway, callsign, com1);
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

    // The voice mode is the ATC mode plus a microphone, a recognizer, a voice and a loudspeaker: it takes the options of those
    // and none of the modes that use only one of them.
    private static ParsedArguments ValidateVoice(
        SimSourceKind? source, string? replay, string? record, double? speed, TimeSpan? duration, LogLevel logLevel, string? atc, string? runway, string? callsign, Fonia.Core.Sim.Frequency? com1, MicRaw mic, SttRaw stt, TtsRaw tts, VoiceRaw voice)
    {
        if (atc is null)
        {
            return Failure("--voice só vale no modo ATC: use também --atc <ICAO>.");
        }

        if (mic.TestPath is not null || stt.TranscribePath is not null || tts.Text is not null || tts.Out is not null)
        {
            return Failure("--voice não combina com --mic-test, --transcribe, --speak nem --speak-out.");
        }

        var parsed = ValidateAtc(source, replay, record, speed, duration, logLevel, atc, runway, callsign, com1);

        return parsed.Options is null
            ? parsed
            : new ParsedArguments(
                parsed.Options with
                {
                    Voice = true,
                    PttKey = mic.Key ?? PushToTalkKey.DefaultName,
                    MicDevice = mic.Device ?? -1,
                    AudioOut = voice.AudioOut ?? -1,
                    SttDirectory = stt.Directory,
                    SttModel = stt.Model,
                    SttGpu = stt.Gpu,
                    TtsVoice = string.IsNullOrEmpty(tts.Voice) ? null : tts.Voice,
                    TtsDirectory = tts.Dir,
                    RadioEffect = tts.Effect ?? Core.Speech.RadioEffect.DefaultIntensity,
                },
                null,
                ShowHelp: false);
    }

    private static ParsedArguments ValidateAtc(
        SimSourceKind? source, string? replay, string? record, double? speed, TimeSpan? duration, LogLevel logLevel, string atc, string? runway, string? callsign, Fonia.Core.Sim.Frequency? com1)
    {
        if (replay is not null || record is not null || speed is not null || duration is not null || source == SimSourceKind.Replay)
        {
            return Failure("--atc não combina com --replay, --record, --speed nem --duration.");
        }

        if (callsign is not null && source is not null)
        {
            return Failure("--callsign dispensa o simulador: remova --sim, ou remova --callsign para usar a matrícula do simulador.");
        }

        return new ParsedArguments(new AppOptions(source ?? SimSourceKind.Msfs, LogLevel: logLevel, AtcAirport: atc, Runway: runway, Callsign: callsign, Com1: com1), null, ShowHelp: false);
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

    internal static string? ParseTtsVoice(string value, out string? problem)
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

    internal static RadioIntensity? ParseRadioEffect(string value, out string? problem)
    {
        problem = null;

        if (Core.Speech.RadioEffect.TryParse(value, out var intensity))
        {
            return intensity;
        }

        problem = $"--radio-effect '{value}' não existe. Use: desligado, leve, média ou forte.";
        return null;
    }

    private static Fonia.Core.Sim.Frequency? ArgumentParserFrequency(string value, out string? problem)
    {
        problem = null;

        if (AtcConsole.TryParseFrequency(value, out var frequency))
        {
            return frequency;
        }

        problem = $"--com1 '{value}' é inválido: use uma frequência em MHz entre 118 e 137 (por exemplo 118.55).";
        return null;
    }

    private static int? ParseMicDevice(string value, out string? problem) => ParseDevice("--mic-device", value, out problem);

    private static int? ParseDevice(string option, string value, out string? problem)
    {
        problem = null;

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var device))
        {
            return device;
        }

        problem = $"{option} '{value}' é inválido: use o número mostrado por --mic-list (0, 1, 2...).";
        return null;
    }

    internal static string? ParsePttKey(string value, out string? problem, string option = "--ptt-key")
    {
        problem = null;

        if (PushToTalkKey.TryParse(value, out _))
        {
            return value;
        }

        problem = $"{option} '{value}' não existe. Exemplos: {string.Join(", ", PushToTalkKey.ExampleNames)} (também letras, números e F1 a F24).";
        return null;
    }

    internal static LogLevel ParseLogLevel(string value, out string? problem)
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

    // --dump-layout stands alone, with --log-level at most: it asks the simulator and ends.
    private static ParsedArguments ParseDumpLayout(string[] args)
    {
        string? icao = null;
        var logLevel = LogLevel.Warning;

        for (var i = 0; i < args.Length; i++)
        {
            var (name, inlineValue) = Split(args[i]);

            if (name is not ("--dump-layout" or "--log-level"))
            {
                return Failure($"--dump-layout não combina com outras opções: use só --dump-layout <ICAO> (e --log-level). Veio '{args[i]}'.");
            }

            var value = inlineValue ?? (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null);

            if (value is null)
            {
                return Failure(name == "--dump-layout"
                    ? "--dump-layout precisa do código do aeroporto (ICAO), por exemplo --dump-layout SBCT."
                    : "--log-level precisa de um valor.");
            }

            if (name == "--log-level")
            {
                logLevel = ParseLogLevel(value, out var problem);

                if (problem is not null)
                {
                    return Failure(problem);
                }

                continue;
            }

            icao = value.Trim().ToUpperInvariant();

            if (icao.Length is < 3 or > 8 || !icao.All(char.IsAsciiLetterOrDigit))
            {
                return Failure($"--dump-layout '{value}' não parece um código de aeroporto (ICAO): use de 3 a 8 letras ou números, como SBCT.");
            }
        }

        return new ParsedArguments(new AppOptions(LogLevel: logLevel, DumpLayoutAirport: icao), null, ShowHelp: false);
    }

    private static ParsedArguments Failure(string message) => new(null, message, ShowHelp: false);
}
