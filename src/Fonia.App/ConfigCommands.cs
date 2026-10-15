namespace Fonia.App;

/// <summary><c>--config</c> and <c>--init-config</c>: where the settings come from, and a first file to edit.</summary>
internal static class ConfigCommands
{
    private const string Template = """
        // Preferências do Fonia. Tudo é opcional: apague as barras do começo da linha de uma opção para usá-la.
        // O que você digita na linha de comando (por exemplo --ptt-key F8) vale mais do que este arquivo.
        // Este arquivo é do seu usuário (%APPDATA%\Fonia): uma atualização do programa não o apaga.
        {
          // Aeroporto sugerido ao abrir o programa sem argumentos (código ICAO).
          // "airport": "SBCT",

          // Indicativo da aeronave, se você não quiser ler a matrícula do simulador.
          // "callsign": "PR-ABC",

          // trace, debug, information, warning, error, critical ou none.
          // "logLevel": "warning",

          "voice": {
            // Tecla do push-to-talk: RControl (padrão), LControl, RShift, RAlt, CapsLock, Space, ScrollLock, Insert, F8, Mouse4, Mouse5...
            // "pttKey": "RControl",

            // Microfone e alto-falante, como numerados por "fonia --mic-list". -1 é o padrão do Windows.
            // "micDevice": -1,
            // "audioOut": -1,

            // Voz do controlador: pf_dora, pm_alex ou aleatoria.
            // "ttsVoice": "aleatoria",

            // Efeito de rádio: desligado, leve, media ou forte.
            // "radioEffect": "media",

            // Reconhecer a fala na placa de vídeo (~0,25 s em vez de ~2 s), usando a GPU do simulador por instantes.
            // "sttGpu": false,

            // Pasta dos modelos de voz (padrão: %LOCALAPPDATA%\Fonia\models).
            // "modelsDir": "D:\\Fonia\\modelos"
          },

          "weather": {
            // false desliga a busca do METAR na internet: sem ele o controlador não diz vento nem ajuste de altímetro.
            // "metar": true
          }
        }
        """;

    /// <summary><c>--config</c>: the files looked at (and whether they exist) and what is set.</summary>
    public static void Show(LoadedSettings loaded, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine("Arquivos de configuração (o do usuário vale mais do que o do programa; a linha de comando, mais do que os dois):");

        foreach (var file in loaded.Files)
        {
            output.WriteLine($"  {file.Path}  ({(file.Exists ? "existe" : "não existe")})");
        }

        output.WriteLine($"Variáveis de ambiente: FONIA_<chave> (por exemplo FONIA_VOICE__PTTKEY).");
        output.WriteLine();

        var settings = loaded.Settings;
        var set = new List<(string Key, string Value)>();

        Add(set, "airport", settings.Airport);
        Add(set, "callsign", settings.Callsign);
        Add(set, "logLevel", settings.LogLevel);
        Add(set, "voice.pttKey", settings.Voice.PttKey);
        Add(set, "voice.micDevice", settings.Voice.MicDevice);
        Add(set, "voice.audioOut", settings.Voice.AudioOut);
        Add(set, "voice.ttsVoice", settings.Voice.TtsVoice);
        Add(set, "voice.radioEffect", settings.Voice.RadioEffect);
        Add(set, "voice.sttGpu", settings.Voice.SttGpu);
        Add(set, "voice.sttModel", settings.Voice.SttModel);
        Add(set, "voice.modelsDir", settings.Voice.ModelsDir);
        Add(set, "weather.metar", settings.Weather.Metar);

        if (set.Count == 0)
        {
            output.WriteLine("Nada configurado: valem os padrões do programa. \"fonia --init-config\" cria um arquivo para editar.");
            return;
        }

        output.WriteLine("Configurado:");

        foreach (var (key, value) in set)
        {
            output.WriteLine($"  {key} = {value}");
        }
    }

    /// <summary><c>--init-config</c>: writes the template as the user's file, unless there is one (it is never overwritten).</summary>
    public static int Init(string path, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (File.Exists(path))
        {
            output.WriteLine($"O arquivo já existe, nada foi alterado: {path}");
            return 0;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Template.ReplaceLineEndings("\r\n") + "\r\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"fonia: não foi possível criar {path}: {ex.Message}");
            return 1;
        }

        output.WriteLine($"Arquivo criado: {path}");
        output.WriteLine("Abra no bloco de notas, tire as barras das opções que quiser e salve. \"fonia --config\" mostra o que está valendo.");
        return 0;
    }

    private static void Add(List<(string Key, string Value)> set, string key, object? value)
    {
        if (value is not null)
        {
            set.Add((key, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
        }
    }
}
