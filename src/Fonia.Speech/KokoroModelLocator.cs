namespace Fonia.Speech;

/// <summary>
/// Where the voice model lives. As with the recognizer, the app does not download it: the user puts the two files in the
/// folder, and a missing file is an error that says where to get it.
/// </summary>
public static class KokoroModelLocator
{
    public const string ModelFileName = "kokoro-v1.0.onnx";
    public const string VoicesFileName = "voices-v1.0.bin";

    private const string DownloadBase = "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/";

    /// <summary>The same folder as the recognizer's model: <c>%LOCALAPPDATA%\Fonia\models</c> on Windows.</summary>
    public static string DefaultDirectory => WhisperModelLocator.DefaultDirectory;

    /// <summary>The paths of the model and of the voices.</summary>
    /// <param name="directory">The folder; the default one when null.</param>
    /// <exception cref="FileNotFoundException">A file is not there; the message says where to download it.</exception>
    public static (string Model, string Voices) Resolve(string? directory)
    {
        var folder = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;
        var model = Path.Combine(folder, ModelFileName);
        var voices = Path.Combine(folder, VoicesFileName);
        var missing = new[] { model, voices }.Where(path => !File.Exists(path)).ToList();

        return missing.Count == 0 ? (model, voices) : throw NotFound(missing);
    }

    internal static FileNotFoundException NotFound(IReadOnlyList<string> missing) => new(
        "Modelo de voz do controlador não encontrado:\n"
        + string.Join('\n', missing.Select(path => $"  {path}\n  baixe em {DownloadBase}{Path.GetFileName(path)}"))
        + $"\nColoque o arquivo nessa pasta, ou indique outra com --tts-dir ({ModelSize(missing)}).",
        missing[0]);

    private static string ModelSize(IReadOnlyList<string> missing) =>
        missing.Any(path => Path.GetFileName(path) == ModelFileName) ? "o modelo tem 325 MB, as vozes 28 MB" : "as vozes têm 28 MB";
}
