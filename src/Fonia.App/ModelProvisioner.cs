using System.Globalization;
using Fonia.Speech;

namespace Fonia.App;

/// <summary>A model a mode needs, and the folder the user asked for it in (null: the usual places).</summary>
internal sealed record ModelNeed(ModelFile File, string? Directory);

/// <summary>Which models each mode of the app uses.</summary>
internal static class ModelNeeds
{
    /// <summary>The recognizer (unless the user chose a model of their own, which is not downloaded), the voice and its voices.</summary>
    public static IReadOnlyList<ModelNeed> ForVoice(AppOptions options) => [.. ForTranscribe(options), .. ForSpeak(options)];

    public static IReadOnlyList<ModelNeed> ForTranscribe(AppOptions options) =>
        options.SttModel is null || string.Equals(options.SttModel, ModelCatalog.WhisperSmall.FileName, StringComparison.OrdinalIgnoreCase)
            ? [new ModelNeed(ModelCatalog.WhisperSmall, options.SttDirectory)]
            : [];

    public static IReadOnlyList<ModelNeed> ForSpeak(AppOptions options) =>
        [new ModelNeed(ModelCatalog.KokoroModel, options.TtsDirectory), new ModelNeed(ModelCatalog.KokoroVoices, options.TtsDirectory)];

    /// <summary>What <c>--download-models --only</c> picks: the recognizer ("stt"), the voice and its voices ("tts"), or everything (null or "all").</summary>
    public static IReadOnlyList<ModelNeed> Selected(string? only, string? directory) => only switch
    {
        "stt" => [new ModelNeed(ModelCatalog.WhisperSmall, directory)],
        "tts" => [new ModelNeed(ModelCatalog.KokoroModel, directory), new ModelNeed(ModelCatalog.KokoroVoices, directory)],
        _ => All(directory),
    };

    /// <summary>Everything the app knows how to download, into one folder.</summary>
    public static IReadOnlyList<ModelNeed> All(string? directory) => [.. ModelCatalog.All.Select(file => new ModelNeed(file, directory))];
}

/// <summary>
/// Makes sure the models are there: tells the user what is missing and, on a terminal, asks before downloading it (the files
/// are big and the connection may be limited). Without a terminal it never downloads by itself: it says how to.
/// </summary>
internal sealed class ModelProvisioner(
    IModelDownloader downloader,
    TextReader input,
    TextWriter output,
    TextWriter error,
    bool interactive,
    IReadOnlyList<string>? searchDirectories = null)
{
    private const string Command = "fonia --download-models";

    /// <summary>The needs whose model is not in place.</summary>
    public List<ModelNeed> Missing(IReadOnlyList<ModelNeed> needs) => [.. needs.Where(need => !IsThere(need))];

    /// <param name="ask">True to ask first (the models were missing when a mode needed them); false for the explicit download command.</param>
    /// <returns>True when every model is there in the end.</returns>
    public async Task<bool> EnsureAsync(IReadOnlyList<ModelNeed> needs, bool ask, CancellationToken ct)
    {
        var missing = Missing(needs);

        if (missing.Count == 0)
        {
            return true;
        }

        output.WriteLine($"Faltam modelos de voz ({Mb(missing.Sum(n => n.File.Size))} MB no total):");

        foreach (var need in missing)
        {
            output.WriteLine($"  - {need.File.FileName}: {need.File.Description}, {Mb(need.File.Size)} MB");
        }

        if (ask && !await Confirm(missing))
        {
            return false;
        }

        var progress = new DownloadProgressPrinter(output, overwrite: interactive);

        foreach (var need in missing)
        {
            try
            {
                await downloader.DownloadAsync(need.File, need.Directory ?? ModelDirectories.Default, progress, ct);
            }
            catch (OperationCanceledException)
            {
                output.WriteLine();
                output.WriteLine("Download cancelado. O que já veio fica guardado e continua de onde parou da próxima vez.");
                return false;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"fonia: {ex.Message}");
                error.WriteLine($"Tente de novo com: {Command} (o que já foi baixado é aproveitado).");
                return false;
            }
        }

        output.WriteLine("Modelos prontos.");
        return true;
    }

    private bool IsThere(ModelNeed need) => need.Directory is { } directory
        ? need.File.IsInstalledIn(directory)
        : (searchDirectories ?? ModelDirectories.Search).Any(need.File.IsInstalledIn);

    private async Task<bool> Confirm(List<ModelNeed> missing)
    {
        if (!interactive)
        {
            error.WriteLine($"fonia: faltam modelos de voz. Baixe com: {Command}");
            return false;
        }

        var folders = string.Join(", ", missing.Select(n => n.Directory ?? ModelDirectories.Default).Distinct());

        await output.WriteAsync($"Baixar agora para {folders}? [S/n] ");
        var answer = (await input.ReadLineAsync() ?? string.Empty).Trim().ToLowerInvariant();

        if (answer is "" or "s" or "sim" or "y" or "yes")
        {
            return true;
        }

        output.WriteLine($"Ok. Para baixar depois: {Command}");
        return false;
    }

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
}

/// <summary>Shows how a download is going: one line rewritten on a terminal, one line per tenth when it goes to a file or a pipe.</summary>
internal sealed class DownloadProgressPrinter(TextWriter output, bool overwrite) : IProgress<DownloadProgress>
{
    private readonly Dictionary<string, int> _lastTenth = [];

    public void Report(DownloadProgress progress)
    {
        var line = Format(progress);
        var finished = progress.Received >= progress.Total;

        if (overwrite)
        {
            output.Write("\r" + line.PadRight(70));

            if (finished)
            {
                output.WriteLine();
            }

            return;
        }

        var tenth = progress.Total == 0 ? 10 : (int)(progress.Received * 10 / progress.Total);

        if (!_lastTenth.TryGetValue(progress.FileName, out var last) || tenth > last)
        {
            _lastTenth[progress.FileName] = tenth;
            output.WriteLine(line);
        }
    }

    internal static string Format(DownloadProgress progress)
    {
        var percent = progress.Total == 0 ? 100 : (int)(progress.Received * 100 / progress.Total);

        return $"  {progress.FileName}  {Mb(progress.Received)} de {Mb(progress.Total)} MB ({percent}%)  {Mb((long)progress.BytesPerSecond)} MB/s";
    }

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
}
