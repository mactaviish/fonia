namespace Fonia.Speech;

/// <summary>
/// Where the speech model lives. The app does not download it: the user puts the file in the folder, and a
/// missing file is an error that says where to get it.
/// </summary>
public static class WhisperModelLocator
{
    public const string DefaultFileName = "ggml-small.bin";

    private const string DownloadBase = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    /// <summary><c>%LOCALAPPDATA%\Fonia\models</c> on Windows.</summary>
    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fonia", "models");

    /// <summary>The full path of the model file.</summary>
    /// <param name="directory">The folder; the default one when null.</param>
    /// <param name="fileName">The model file; <see cref="DefaultFileName"/> when null. A name only, no folders.</param>
    /// <exception cref="FileNotFoundException">The file is not there; the message says where to download it.</exception>
    public static string Resolve(string? directory, string? fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? DefaultFileName : fileName.Trim();

        if (name != Path.GetFileName(name))
        {
            throw new ArgumentException($"O nome do modelo deve ser só o nome do arquivo, sem pasta: '{name}'. Use --stt-dir para a pasta.", nameof(fileName));
        }

        var path = Path.Combine(string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory, name);

        return File.Exists(path) ? path : throw NotFound(path);
    }

    internal static FileNotFoundException NotFound(string path) => new(
        $"Modelo de voz não encontrado: {path}\n"
        + $"Baixe {Path.GetFileName(path)} em {DownloadBase}{Path.GetFileName(path)} (466 MB para o ggml-small.bin) "
        + "e coloque-o nessa pasta, ou indique outra com --stt-dir.",
        path);
}
