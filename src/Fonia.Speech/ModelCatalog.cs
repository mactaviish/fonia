namespace Fonia.Speech;

/// <summary>A model file the app can download: where from, how big and which bytes it must have.</summary>
/// <param name="Sha256">The SHA-256 of the file, in hex. A download that does not match it is thrown away.</param>
/// <param name="Description">What the file is for, in words for the user.</param>
public sealed record ModelFile(string FileName, Uri Url, string Sha256, long Size, string Description)
{
    /// <summary>The file is in the folder and has the size of the model (a copy that was cut short does not count).</summary>
    public bool IsInstalledIn(string directory) =>
        new FileInfo(Path.Combine(directory, FileName)) is { Exists: true } info && info.Length == Size;
}

/// <summary>
/// The models the app needs, and how to check them. The files come from their owners and are not redistributed
/// by the project; the hashes were checked against what the owners publish (docs/decisions/speech.md).
/// </summary>
public static class ModelCatalog
{
    /// <summary>The recognizer. MIT. The hash is the one Hugging Face publishes for the file (its linked ETag).</summary>
    public static ModelFile WhisperSmall { get; } = new(
        "ggml-small.bin",
        new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin"),
        "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b",
        487_601_967,
        "reconhecimento de voz (Whisper small, licença MIT)");

    /// <summary>The voice model. Apache-2.0. GitHub does not publish a hash for release files: this one was computed twice, from two downloads.</summary>
    public static ModelFile KokoroModel { get; } = new(
        "kokoro-v1.0.onnx",
        new Uri("https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.onnx"),
        "7d5df8ecf7d4b1878015a32686053fd0eebe2bc377234608764cc0ef3636a6c5",
        325_532_387,
        "voz do controlador (Kokoro-82M, licença Apache-2.0)");

    public static ModelFile KokoroVoices { get; } = new(
        "voices-v1.0.bin",
        new Uri("https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin"),
        "bca610b8308e8d99f32e6fe4197e7ec01679264efed0cac9140fe9c29f1fbf7d",
        28_214_398,
        "vozes do controlador (Kokoro-82M, licença Apache-2.0)");

    public static IReadOnlyList<ModelFile> All { get; } = [WhisperSmall, KokoroModel, KokoroVoices];

    /// <summary>The catalog entry with this file name, or null for a model the user brought.</summary>
    public static ModelFile? Find(string fileName) =>
        All.FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The model is there and usable: a model of the catalog must have its size (a copy cut short is not usable), and a
    /// model that is not in the catalog (one the user brought) only has to exist.
    /// </summary>
    public static bool IsPresent(string path)
    {
        var info = new FileInfo(path);

        if (!info.Exists)
        {
            return false;
        }

        return Find(info.Name) is not { } file || info.Length == file.Size;
    }
}
