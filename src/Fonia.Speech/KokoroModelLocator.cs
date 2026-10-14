namespace Fonia.Speech;

/// <summary>
/// Where the voice model lives: in <c>models</c> next to the program, in the user's own folder (where
/// <c>fonia --download-models</c> puts it) or in the folder the user names, as with the recognizer.
/// </summary>
public static class KokoroModelLocator
{
    public const string ModelFileName = "kokoro-v1.0.onnx";
    public const string VoicesFileName = "voices-v1.0.bin";

    private const string DownloadBase = "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/";

    /// <summary>The same folder as the recognizer's model: <c>%LOCALAPPDATA%\Fonia\models</c> on Windows.</summary>
    public static string DefaultDirectory => ModelDirectories.Default;

    /// <summary>The paths of the model and of the voices.</summary>
    /// <param name="directory">The folder; when null, the folders of <paramref name="searchDirectories"/> are searched in order.</param>
    /// <param name="searchDirectories">Where to look when no folder was given; <see cref="ModelDirectories.Search"/> by default.</param>
    /// <exception cref="FileNotFoundException">A file is not there; the message says how to get it.</exception>
    public static (string Model, string Voices) Resolve(string? directory, IReadOnlyList<string>? searchDirectories = null)
    {
        var folders = string.IsNullOrWhiteSpace(directory) ? searchDirectories ?? ModelDirectories.Search : [directory];

        foreach (var folder in folders)
        {
            var model = Path.Combine(folder, ModelFileName);
            var voices = Path.Combine(folder, VoicesFileName);

            if (ModelCatalog.IsPresent(model) && ModelCatalog.IsPresent(voices))
            {
                return (model, voices);
            }
        }

        // Nothing complete anywhere: say what is missing where the files would go.
        var target = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;
        var expected = new[] { Path.Combine(target, ModelFileName), Path.Combine(target, VoicesFileName) };

        throw NotFound([.. expected.Where(path => !ModelCatalog.IsPresent(path))]);
    }

    internal static FileNotFoundException NotFound(IReadOnlyList<string> missing) => new(
        "Modelo de voz do controlador não encontrado:\n"
        + string.Join('\n', missing.Select(path => $"  {path}\n  baixe em {DownloadBase}{Path.GetFileName(path)}"))
        + $"\nColoque o arquivo nessa pasta, ou indique outra com --tts-dir ({ModelSize(missing)}). Ou deixe o app baixar: fonia --download-models.",
        missing[0]);

    private static string ModelSize(IReadOnlyList<string> missing) =>
        missing.Any(path => Path.GetFileName(path) == ModelFileName) ? "o modelo tem 325 MB, as vozes 28 MB" : "as vozes têm 28 MB";
}
