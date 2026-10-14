namespace Fonia.Speech;

/// <summary>
/// Where the speech model lives: in <c>models</c> next to the program, or in the user's own folder (where
/// <c>fonia --download-models</c> puts it), or in the folder the user names. A missing file is an error that says how to get it.
/// </summary>
public static class WhisperModelLocator
{
    public const string DefaultFileName = "ggml-small.bin";

    private const string DownloadBase = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    /// <summary><c>%LOCALAPPDATA%\Fonia\models</c> on Windows.</summary>
    public static string DefaultDirectory => ModelDirectories.Default;

    /// <summary>The full path of the model file.</summary>
    /// <param name="directory">The folder; when null, the folders of <paramref name="searchDirectories"/> are searched in order.</param>
    /// <param name="searchDirectories">Where to look when no folder was given; <see cref="ModelDirectories.Search"/> by default.</param>
    /// <param name="fileName">The model file; <see cref="DefaultFileName"/> when null. A name only, no folders.</param>
    /// <exception cref="FileNotFoundException">The file is not there; the message says where to download it.</exception>
    public static string Resolve(string? directory, string? fileName, IReadOnlyList<string>? searchDirectories = null)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? DefaultFileName : fileName.Trim();

        if (name != Path.GetFileName(name))
        {
            throw new ArgumentException($"O nome do modelo deve ser só o nome do arquivo, sem pasta: '{name}'. Use --stt-dir para a pasta.", nameof(fileName));
        }

        if (!string.IsNullOrWhiteSpace(directory))
        {
            var chosen = Path.Combine(directory, name);

            return ModelCatalog.IsPresent(chosen) ? chosen : throw NotFound(chosen);
        }

        var found = (searchDirectories ?? ModelDirectories.Search).Select(folder => Path.Combine(folder, name)).FirstOrDefault(ModelCatalog.IsPresent);

        return found ?? throw NotFound(Path.Combine(DefaultDirectory, name));
    }

    internal static FileNotFoundException NotFound(string path) => new(
        $"Modelo de voz não encontrado: {path}\n"
        + $"Baixe {Path.GetFileName(path)} em {DownloadBase}{Path.GetFileName(path)} (466 MB para o ggml-small.bin) "
        + "e coloque-o nessa pasta, ou indique outra com --stt-dir. Ou deixe o app baixar: fonia --download-models.",
        path);
}
