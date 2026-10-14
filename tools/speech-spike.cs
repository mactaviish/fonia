#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false
#:property NoWarn=$(NoWarn);CA1305;CA1307;CA1310;CA1822;CS8981
#:package Whisper.net@1.9.1
#:package Whisper.net.Runtime@1.9.1
#:package Whisper.net.Runtime.Vulkan@1.9.1
#:package Whisper.net.Runtime.Cuda12@1.9.1
#:package NAudio@2.2.1
#:project ../src/Fonia.Core/Fonia.Core.csproj

// Spike do M3: mede as opções de voz (STT e TTS) na máquina onde roda. Não faz parte da solução nem do app.
//
//   dotnet run tools/speech-spike.cs -- record                  grava as frases de tools/speech-spike/phrases.txt com o microfone
//   dotnet run tools/speech-spike.cs -- synth                   gera as mesmas frases com a voz do Windows (teste do processo, sem microfone)
//   dotnet run tools/speech-spike.cs -- stt --model small       transcreve as gravações e mede acerto e tempo
//   dotnet run tools/speech-spike.cs -- tts                     mede as vozes do Windows falando frases do controlador
//
// Os modelos vão para %TEMP%\fonia-speech-spike\models e as gravações para tools/speech-spike/recordings (fora do git).

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;
using Windows.Media.SpeechSynthesis;

var repository = Path.GetFullPath(Path.Combine(ScriptDirectory(), ".."));
var kit = Path.Combine(repository, "tools", "speech-spike");
var work = Path.Combine(Path.GetTempPath(), "fonia-speech-spike");

var command = args.Length > 0 ? args[0] : "help";
var options = ParseOptions(args.Skip(1).ToArray());

return command switch
{
    "record" => Record(options.Get("dir", Path.Combine(kit, "recordings"))),
    "synth" => await SynthPhrasesAsync(options.Get("dir", Path.Combine(work, "synthetic")), options.Get("voice", "Maria")),
    "stt" => await SttAsync(options),
    "tts" => await TtsAsync(Path.Combine(work, "tts")),
    _ => Help(),
};

int Help()
{
    Console.WriteLine("Comandos: record | synth | stt --model tiny|base|small [--quant none|q5_1|q8_0] [--dir pasta] [--prompt] [--threads n] [--runtime cpu|vulkan|cuda] | tts");
    return 0;
}

List<Phrase> LoadPhrases()
{
    var phrases = new List<Phrase>();

    foreach (var line in File.ReadAllLines(Path.Combine(kit, "phrases.txt")))
    {
        if (line.TrimStart().StartsWith('#') || string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var parts = line.Split('|', 4, StringSplitOptions.TrimEntries);
        phrases.Add(new Phrase(parts[0], parts[1], parts[2], parts[3]));
    }

    return phrases;
}

int Record(string dir)
{
    Directory.CreateDirectory(dir);
    Console.WriteLine($"Gravando em {dir}. Para cada frase: Enter para começar, Enter para parar. 'r' + Enter repete a anterior, 'p' + Enter pula, 'q' + Enter sai.");

    var phrases = LoadPhrases();

    for (var i = 0; i < phrases.Count; i++)
    {
        var phrase = phrases[i];
        var path = Path.Combine(dir, $"{phrase.Id}.wav");

        Console.WriteLine();
        Console.WriteLine($"[{i + 1}/{phrases.Count}] {phrase.Text}");
        Console.Write("Enter para gravar (p pula, q sai): ");
        var answer = (Console.ReadLine() ?? "q").Trim().ToLowerInvariant();

        if (answer == "q")
        {
            break;
        }

        if (answer == "p")
        {
            continue;
        }

        using var input = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1) };
        using var writer = new WaveFileWriter(path, input.WaveFormat);
        input.DataAvailable += (_, e) => writer.Write(e.Buffer, 0, e.BytesRecorded);
        input.StartRecording();
        Console.Write("Gravando... Enter para parar: ");
        Console.ReadLine();
        input.StopRecording();
        writer.Flush();

        Console.WriteLine($"Salvo ({writer.TotalTime.TotalSeconds:F1} s). 'r' + Enter para regravar, Enter para seguir.");

        if ((Console.ReadLine() ?? string.Empty).Trim().Equals("r", StringComparison.OrdinalIgnoreCase))
        {
            i--;
        }
    }

    return 0;
}

async Task<int> SynthPhrasesAsync(string dir, string voiceName)
{
    Directory.CreateDirectory(dir);
    using var synthesizer = new SpeechSynthesizer { Voice = FindVoice(voiceName) };

    foreach (var phrase in LoadPhrases())
    {
        await SaveSpeechAsync(synthesizer, phrase.Text, Path.Combine(dir, $"{phrase.Id}.wav"), 16000);
    }

    Console.WriteLine($"Frases geradas com a voz {synthesizer.Voice.DisplayName} em {dir}. Use: stt --dir \"{dir}\"");
    return 0;
}

async Task<int> SttAsync(Options o)
{
    var model = o.Get("model", "small");
    var quant = o.Get("quant", "none");
    var dir = o.Get("dir", Path.Combine(kit, "recordings"));
    var threads = int.Parse(o.Get("threads", Math.Max(1, Environment.ProcessorCount / 2).ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
    var usePrompt = o.Has("prompt");
    var runtime = o.Get("runtime", "cpu");

    RuntimeOptions.RuntimeLibraryOrder = runtime switch
    {
        "vulkan" => [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu],
        "cuda" => [RuntimeLibrary.Cuda, RuntimeLibrary.Cpu],
        _ => [RuntimeLibrary.Cpu],
    };

    var type = model switch
    {
        "tiny" => GgmlType.Tiny,
        "base" => GgmlType.Base,
        "small" => GgmlType.Small,
        "medium" => GgmlType.Medium,
        _ => throw new ArgumentException($"modelo desconhecido: {model}"),
    };
    var quantization = quant switch
    {
        "q5_1" => QuantizationType.Q5_1,
        "q8_0" => QuantizationType.Q8_0,
        _ => QuantizationType.NoQuantization,
    };

    var modelPath = Path.Combine(work, "models", $"ggml-{model}-{quant}.bin");
    Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);

    if (!File.Exists(modelPath))
    {
        Console.WriteLine($"Baixando o modelo {model} ({quant}) para {modelPath} ...");
        await using var download = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(type, quantization);
        await using var file = File.Create(modelPath);
        await download.CopyToAsync(file);
    }

    var sizeMb = new FileInfo(modelPath).Length / 1024.0 / 1024.0;
    Console.WriteLine($"Modelo {model} {quant}: {sizeMb:F0} MB | runtime {runtime} | {threads} threads | prompt {(usePrompt ? "sim" : "não")}");

    var loadWatch = Stopwatch.StartNew();
    using var factory = WhisperFactory.FromPath(modelPath);
    var builder = factory.CreateBuilder().WithLanguage("pt").WithThreads(threads).WithGreedySamplingStrategy(g => g.WithBestOf(1)).WithTemperature(0f);

    if (usePrompt)
    {
        builder = builder.WithPrompt("Comunicação de rádio aeronáutica em português. Solo, Torre, pushback, acionamento, táxi, pista, ponto de espera, QNH. Papa Romeo Alfa Bravo Charlie. Pista uno cinco.");
    }

    await using var processor = builder.Build();
    loadWatch.Stop();
    Console.WriteLine($"Carregou em {loadWatch.ElapsedMilliseconds} ms.");

    var phrases = LoadPhrases().Where(p => File.Exists(Path.Combine(dir, $"{p.Id}.wav"))).ToList();

    if (phrases.Count == 0)
    {
        Console.Error.WriteLine($"Nenhuma gravação em {dir}. Rode 'record' (ou 'synth').");
        return 2;
    }

    var nlu = new RuleBasedNlu();
    var times = new List<double>();
    var audioSeconds = new List<double>();
    var wers = new List<double>();
    var intentHits = 0;

    // The first call pays for warming up the runtime; it is run once and not counted.
    await TranscribeAsync(processor, Path.Combine(dir, $"{phrases[0].Id}.wav"));

    foreach (var phrase in phrases)
    {
        var path = Path.Combine(dir, $"{phrase.Id}.wav");
        var duration = AudioDuration(path);

        var watch = Stopwatch.StartNew();
        var text = await TranscribeAsync(processor, path);
        watch.Stop();

        var wer = WordErrorRate(phrase.Text, text);
        var understood = Describe(nlu.Interpret(text, FlightFor(phrase.Context)));
        var hit = understood == phrase.Expected;

        times.Add(watch.Elapsed.TotalMilliseconds);
        audioSeconds.Add(duration);
        wers.Add(wer);
        intentHits += hit ? 1 : 0;

        Console.WriteLine($"{phrase.Id} {(hit ? "ok " : "ERRO")} {watch.ElapsedMilliseconds,5} ms  WER {wer,5:P0}  \"{text}\"");

        if (!hit)
        {
            Console.WriteLine($"      esperado {phrase.Expected}, entendeu {understood}");
        }
    }

    var peak = Process.GetCurrentProcess().PeakWorkingSet64 / 1024.0 / 1024.0;
    times.Sort();
    Console.WriteLine();
    Console.WriteLine($"RESUMO {model} {quant} {runtime}: {phrases.Count} frases");
    Console.WriteLine($"  intenção entendida: {intentHits}/{phrases.Count} ({(double)intentHits / phrases.Count:P0})");
    Console.WriteLine($"  WER médio: {wers.Average():P1}");
    Console.WriteLine($"  tempo por frase: média {times.Average():F0} ms | mediana {times[times.Count / 2]:F0} ms | pior {times[^1]:F0} ms");
    Console.WriteLine($"  duração média do áudio: {audioSeconds.Average():F1} s | tempo/áudio {times.Average() / 1000 / audioSeconds.Average():F2}x");
    Console.WriteLine($"  memória de pico do processo: {peak:F0} MB");
    return 0;
}

async Task<int> TtsAsync(string dir)
{
    Directory.CreateDirectory(dir);

    // What the controller says in the M2 flow, as the phraseology writes it.
    var sentences = new[]
    {
        "PAPA ROMEO ALFA BRAVO CHARLIE, acionamento aprovado, chame pronto para o táxi.",
        "PAPA ROMEO ALFA BRAVO CHARLIE, autorizado pushback e acionamento, chame pronto para o táxi.",
        "PAPA ROMEO ALFA BRAVO CHARLIE, autorizado táxi para o ponto de espera da PISTA UNO CINCO.",
        "PAPA ROMEO ALFA BRAVO CHARLIE, negativo, autorizado táxi para o ponto de espera da PISTA UNO CINCO.",
        "Repita.",
        "PAPA ROMEO ALFA BRAVO CHARLIE, prossiga.",
    };

    foreach (var voice in SpeechSynthesizer.AllVoices.Where(v => v.Language.StartsWith("pt", StringComparison.OrdinalIgnoreCase)))
    {
        using var synthesizer = new SpeechSynthesizer { Voice = voice };
        var times = new List<double>();
        var durations = new List<double>();

        await SaveSpeechAsync(synthesizer, sentences[0], Path.Combine(dir, "warmup.wav"), 22050);

        for (var i = 0; i < sentences.Length; i++)
        {
            var path = Path.Combine(dir, $"{voice.DisplayName.Replace(' ', '-')}-{i + 1}.wav");
            var watch = Stopwatch.StartNew();
            await SaveSpeechAsync(synthesizer, sentences[i], path, 22050);
            watch.Stop();

            times.Add(watch.Elapsed.TotalMilliseconds);
            durations.Add(AudioDuration(path));
        }

        Console.WriteLine($"{voice.DisplayName} ({voice.Language}): síntese média {times.Average():F0} ms para {durations.Average():F1} s de áudio ({times.Average() / 1000 / durations.Average():F2}x do tempo falado); pior {times.Max():F0} ms");
    }

    Console.WriteLine($"Áudios em {dir} (ouça para avaliar a qualidade).");
    return 0;
}

VoiceInformation FindVoice(string name) =>
    SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase) && v.Language.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
    ?? throw new ArgumentException($"nenhuma voz pt-BR com '{name}'");

async Task SaveSpeechAsync(SpeechSynthesizer synthesizer, string text, string path, int sampleRate)
{
    using var stream = await synthesizer.SynthesizeTextToStreamAsync(text);
    var bytes = new byte[stream.Size];
    using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
    await reader.LoadAsync((uint)stream.Size);
    reader.ReadBytes(bytes);

    using var source = new WaveFileReader(new MemoryStream(bytes));
    var mono = source.WaveFormat.Channels > 1 ? new StereoToMonoSampleProvider(source.ToSampleProvider()) : source.ToSampleProvider();
    WaveFileWriter.CreateWaveFile16(path, new WdlResamplingSampleProvider(mono, sampleRate));
}

async Task<string> TranscribeAsync(WhisperProcessor processor, string path)
{
    // Whisper wants 16 kHz mono; the recordings and the synthetic phrases already are, but any file is accepted.
    var wav = new MemoryStream();
    using (var reader = new AudioFileReader(path))
    {
        ISampleProvider samples = reader.WaveFormat.Channels > 1 ? new StereoToMonoSampleProvider(reader) : reader;
        samples = reader.WaveFormat.SampleRate == 16000 ? samples : new WdlResamplingSampleProvider(samples, 16000);
        WaveFileWriter.WriteWavFileToStream(wav, new SampleToWaveProvider16(samples));
    }

    wav.Position = 0;
    var text = new StringBuilder();

    await foreach (var segment in processor.ProcessAsync(wav))
    {
        text.Append(segment.Text);
    }

    return text.ToString().Trim();
}

double AudioDuration(string path)
{
    using var reader = new AudioFileReader(path);
    return reader.TotalTime.TotalSeconds;
}

FlightContext FlightFor(string context) => context switch
{
    "startup" => new FlightContext("PRABC", FlightPhase.StartupApproved, "SBCT", new StartupApproved()),
    "pushback" => new FlightContext("PRABC", FlightPhase.StartupApproved, "SBCT", new PushbackApproved(true)),
    "taxi" => new FlightContext("PRABC", FlightPhase.Taxiing, "SBCT", new TaxiClearance("15")),
    _ => new FlightContext("PRABC", FlightPhase.Parked, "SBCT", null),
};

string Describe(Intent intent)
{
    var kind = intent switch
    {
        RequestPushback p => p.WithStartup ? "pushback+startup" : "pushback",
        RequestStartup => "startup",
        RequestTaxi => "taxi",
        Readback => "readback",
        SayAgain => "sayagain",
        CallsignOnly => "callsign",
        _ => "unknown",
    };

    return $"{kind}@{intent.Callsign ?? "-"}";
}

// Word error rate on the words as the NLU sees them: no accents or punctuation, and numbers as digits ("um cinco" = "15").
double WordErrorRate(string reference, string hypothesis)
{
    var r = Canonical(reference);
    var h = Canonical(hypothesis);
    var d = new int[r.Count + 1, h.Count + 1];

    for (var i = 0; i <= r.Count; i++)
    {
        d[i, 0] = i;
    }

    for (var j = 0; j <= h.Count; j++)
    {
        d[0, j] = j;
    }

    for (var i = 1; i <= r.Count; i++)
    {
        for (var j = 1; j <= h.Count; j++)
        {
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (r[i - 1] == h[j - 1] ? 0 : 1));
        }
    }

    return r.Count == 0 ? 0 : (double)d[r.Count, h.Count] / r.Count;
}

List<string> Canonical(string text)
{
    var tokens = SpokenTokens.Tokenize(text);
    var words = new List<string>();
    var number = new StringBuilder();

    for (var i = 0; i < tokens.Count; i++)
    {
        var index = i;

        if (SpokenCode.TryRead(tokens, ref index, out var value) && value.All(char.IsAsciiDigit) && value.Length > 0)
        {
            number.Append(value);
            i = index - 1;
            continue;
        }

        if (number.Length > 0)
        {
            words.Add(number.ToString());
            number.Clear();
        }

        words.Add(tokens[i]);
    }

    if (number.Length > 0)
    {
        words.Add(number.ToString());
    }

    return words;
}

Options ParseOptions(string[] values)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < values.Length; i++)
    {
        if (!values[i].StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var key = values[i][2..];
        map[key] = i + 1 < values.Length && !values[i + 1].StartsWith("--", StringComparison.Ordinal) ? values[++i] : "true";
    }

    return new Options(map);
}

static string ScriptDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

internal sealed record Phrase(string Id, string Context, string Expected, string Text);

internal sealed class Options(Dictionary<string, string> map)
{
    public string Get(string key, string fallback) => map.TryGetValue(key, out var value) ? value : fallback;

    public bool Has(string key) => map.ContainsKey(key);
}
