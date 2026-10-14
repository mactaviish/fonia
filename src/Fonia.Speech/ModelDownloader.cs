using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Fonia.Speech;

/// <summary>How far a download has gone.</summary>
/// <param name="BytesPerSecond">The speed of this session (what was already on the disk from before does not count).</param>
public readonly record struct DownloadProgress(string FileName, long Received, long Total, double BytesPerSecond);

/// <summary>The file that was downloaded is not the one expected: it was thrown away.</summary>
public sealed class ModelIntegrityException(string message) : IOException(message);

/// <summary>There is not enough room on the disk for the file.</summary>
public sealed class InsufficientDiskSpaceException(string message) : IOException(message);

/// <summary>Downloads a model to a folder.</summary>
public interface IModelDownloader
{
    /// <summary>Makes <paramref name="file"/> available in <paramref name="directory"/>, or throws without leaving a wrong file there.</summary>
    Task DownloadAsync(ModelFile file, string directory, IProgress<DownloadProgress>? progress, CancellationToken ct);
}

public sealed class ModelDownloaderOptions
{
    /// <summary>The free space where a path is; the real disk by default.</summary>
    public Func<string, long> FreeSpace { get; init; } = path => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? path).AvailableFreeSpace;

    /// <summary>A connection that sends nothing for this long is given up (the downloaded part is kept for the next try).</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Room to leave free on the disk besides the file itself.</summary>
    public long SafetyMargin { get; init; } = 200L * 1024 * 1024;

    /// <summary>How often progress is reported; zero reports after every block.</summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(250);
}

/// <summary>
/// Downloads a model safely: into a ".part" file that only becomes the model after its SHA-256 matches, resuming from where
/// an earlier try stopped, and leaving nothing wrong in the folder. It uses no library besides the framework's.
/// </summary>
public sealed class ModelDownloader(HttpClient http, ModelDownloaderOptions? options = null, TimeProvider? timeProvider = null) : IModelDownloader
{
    private readonly ModelDownloaderOptions _options = options ?? new ModelDownloaderOptions();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task DownloadAsync(ModelFile file, string directory, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (file.IsInstalledIn(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, file.FileName);
        var part = target + ".part";

        var have = File.Exists(part) ? new FileInfo(part).Length : 0;

        if (have > file.Size)
        {
            File.Delete(part);
            have = 0;
        }

        var needed = file.Size - have + _options.SafetyMargin;
        var free = _options.FreeSpace(directory);

        if (free < needed)
        {
            throw new InsufficientDiskSpaceException(
                $"Sem espaço em disco para {file.FileName}: faltam {Mb(needed)} MB livres em '{directory}' e só há {Mb(free)} MB.");
        }

        if (have < file.Size)
        {
            have = await Receive(file, part, have, progress, ct);
        }

        if (have != file.Size)
        {
            throw new IOException($"O download de {file.FileName} terminou incompleto ({Mb(have)} de {Mb(file.Size)} MB). Tente de novo: ele continua de onde parou.");
        }

        await Verify(file, part, ct);
        File.Move(part, target, overwrite: true);
    }

    // Returns how many bytes the ".part" file has in the end.
    private async Task<long> Receive(ModelFile file, string part, long have, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        using var response = await Request(file, have, ct);

        // The server cannot give the range asked for (the file changed, or it does not do ranges): start from the beginning.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && have > 0)
        {
            File.Delete(part);
            return await Receive(file, part, 0, progress, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"O servidor respondeu {(int)response.StatusCode} ({response.ReasonPhrase}) ao baixar {file.FileName} de {file.Url}.");
        }

        // 206 continues the file; 200 is the whole file again, even though a range was asked.
        var resumed = response.StatusCode == HttpStatusCode.PartialContent && have > 0;
        var received = resumed ? have : 0;
        var started = _time.GetTimestamp();
        var lastReport = started;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await ReadWithStallLimit(source, buffer, ct);

            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;

            var now = _time.GetTimestamp();

            if (_time.GetElapsedTime(lastReport, now) >= _options.ProgressInterval)
            {
                lastReport = now;
                var seconds = Math.Max(_time.GetElapsedTime(started, now).TotalSeconds, 0.001);
                progress?.Report(new DownloadProgress(file.FileName, received, file.Size, (received - (resumed ? have : 0)) / seconds));
            }
        }

        await output.FlushAsync(ct);
        progress?.Report(new DownloadProgress(file.FileName, received, file.Size, 0));
        return received;
    }

    private async Task<HttpResponseMessage> Request(ModelFile file, long from, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, file.Url);

        if (from > 0)
        {
            message.Headers.Range = new RangeHeaderValue(from, null);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_options.StallTimeout);

        try
        {
            return await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw Stalled(file);
        }
    }

    private async Task<int> ReadWithStallLimit(Stream source, byte[] buffer, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_options.StallTimeout);

        try
        {
            return await source.ReadAsync(buffer, limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException(
                $"Sem receber dados há {_options.StallTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s. Verifique a conexão e tente de novo: o download continua de onde parou.");
        }
    }

    private IOException Stalled(ModelFile file) =>
        new($"O servidor de {file.FileName} ficou sem receber dados há {_options.StallTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s. Tente de novo.");

    private static async Task Verify(ModelFile file, string part, CancellationToken ct)
    {
        await using var stream = File.OpenRead(part);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();

        if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            stream.Close();
            File.Delete(part);

            throw new ModelIntegrityException(
                $"O arquivo baixado de {file.FileName} não é o esperado e foi descartado (SHA-256 {hash}, esperado {file.Sha256}). "
                + "Pode ser um download corrompido: tente de novo.");
        }
    }

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
}
