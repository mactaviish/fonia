using Fonia.Speech;

namespace Fonia.App;

/// <summary>Getting the models, wired to the real network. Only the composition root and the modes that need a model call it.</summary>
internal static class ModelCommands
{
    /// <summary><c>--download-models</c>: downloads what is missing, without asking.</summary>
    public static async Task<int> DownloadAsync(AppOptions options, TextReader input, TextWriter output, TextWriter error, bool interactive, CancellationToken ct)
    {
        using var http = CreateHttpClient();
        var provisioner = new ModelProvisioner(new ModelDownloader(http), input, output, error, interactive);
        var needs = ModelNeeds.Selected(options.OnlyModels, options.ModelsDir);

        if (provisioner.Missing(needs).Count == 0)
        {
            output.WriteLine("Os modelos de voz já estão instalados. Nada a baixar.");
            return 0;
        }

        return await provisioner.EnsureAsync(needs, ask: false, ct) ? 0 : 1;
    }

    /// <summary>What a mode that needs models does first: if some are missing, tell the user and offer to download them.</summary>
    /// <returns>True when the models are there and the mode can go on.</returns>
    public static async Task<bool> EnsureAsync(IReadOnlyList<ModelNeed> needs, TextReader input, TextWriter output, TextWriter error, bool interactive, CancellationToken ct)
    {
        using var http = CreateHttpClient();

        return await new ModelProvisioner(new ModelDownloader(http), input, output, error, interactive).EnsureAsync(needs, ask: true, ct);
    }

    private static HttpClient CreateHttpClient()
    {
        // A download of hundreds of megabytes is not bounded by one timeout: the downloader gives up on a connection that goes quiet.
        var http = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(30) }) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Fonia");
        return http;
    }
}
