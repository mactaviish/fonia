using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Fonia.Speech.Tests;

public sealed class ModelDownloaderTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fonia-download-" + Guid.NewGuid().ToString("N"));

    // 100 KB of content that is not the same byte over and over, so that a wrong resume shows.
    private static readonly byte[] Content = Enumerable.Range(0, 100_000).Select(i => (byte)((i * 31) + (i / 251))).ToArray();

    private static readonly ModelFile Model = new(
        "model.bin",
        new Uri("https://example.test/model.bin"),
        Convert.ToHexString(SHA256.HashData(Content)).ToLowerInvariant(),
        Content.Length,
        "A test model");

    public ModelDownloaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private string Target => Path.Combine(_directory, "model.bin");

    private string Part => Target + ".part";

    /// <summary>A server that has the model, with the failures a download can have.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public byte[] Body { get; set; } = Content;

        public bool IgnoreRange { get; set; }

        public bool AlwaysRangeNotSatisfiable { get; set; }

        public int? DropAfterBytes { get; set; }

        public bool Stall { get; set; }

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public List<long?> RangeStarts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From;
            RangeStarts.Add(from);

            if (Status != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(Status));
            }

            if (from is not null && AlwaysRangeNotSatisfiable)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
            }

            var partial = from is not null && !IgnoreRange;
            var start = partial ? (int)from!.Value : 0;
            var slice = Body[start..];
            Stream stream = Stall ? new StallingStream() : new LimitedStream(slice, DropAfterBytes);
            var response = new HttpResponseMessage(partial ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentLength = slice.Length;

            if (partial)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, Body.Length - 1, Body.Length);
            }

            return Task.FromResult(response);
        }
    }

    // Gives the bytes and then, when asked to, breaks the connection.
    private sealed class LimitedStream(byte[] data, int? dropAfter) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (dropAfter is { } limit && _position >= limit)
            {
                throw new IOException("The connection was closed.");
            }

            var available = Math.Min(Math.Min(buffer.Length, 8192), dropAfter is { } l ? l - _position : int.MaxValue);
            available = Math.Min(available, data.Length - _position);
            data.AsSpan(_position, available).CopyTo(buffer);
            _position += available;
            return available;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // A connection that stays open and sends nothing.
    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static ModelDownloader Create(FakeServer server, long freeSpace = long.MaxValue, TimeSpan? stall = null) =>
        new(new HttpClient(server), new ModelDownloaderOptions { FreeSpace = _ => freeSpace, StallTimeout = stall ?? TimeSpan.FromSeconds(30), ProgressInterval = TimeSpan.Zero });

    private sealed class Collect : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Reports { get; } = [];

        public void Report(DownloadProgress value) => Reports.Add(value);
    }

    [Fact]
    public async Task It_downloads_the_file_and_leaves_only_the_finished_file()
    {
        await Create(new FakeServer()).DownloadAsync(Model, _directory, null, Ct);

        (await File.ReadAllBytesAsync(Target, Ct)).ShouldBe(Content);
        File.Exists(Part).ShouldBeFalse();
    }

    [Fact]
    public async Task It_creates_the_folder_when_it_does_not_exist()
    {
        var nested = Path.Combine(_directory, "a", "b");

        await Create(new FakeServer()).DownloadAsync(Model, nested, null, Ct);

        File.Exists(Path.Combine(nested, "model.bin")).ShouldBeTrue();
    }

    [Fact]
    public async Task It_reports_progress_up_to_the_whole_file()
    {
        var progress = new Collect();

        await Create(new FakeServer()).DownloadAsync(Model, _directory, progress, Ct);

        progress.Reports.ShouldNotBeEmpty();
        progress.Reports.ShouldAllBe(r => r.FileName == "model.bin" && r.Total == Content.Length);
        progress.Reports.Select(r => r.Received).ShouldBeInOrder();
        progress.Reports[^1].Received.ShouldBe(Content.Length);
    }

    [Fact]
    public async Task A_file_that_is_already_there_is_not_downloaded_again()
    {
        await File.WriteAllBytesAsync(Target, Content, Ct);
        var server = new FakeServer();

        await Create(server).DownloadAsync(Model, _directory, null, Ct);

        server.RangeStarts.ShouldBeEmpty("no request at all");
    }

    [Fact]
    public async Task A_file_of_the_wrong_size_is_replaced()
    {
        await File.WriteAllBytesAsync(Target, Content[..500], Ct);

        await Create(new FakeServer()).DownloadAsync(Model, _directory, null, Ct);

        (await File.ReadAllBytesAsync(Target, Ct)).ShouldBe(Content);
    }

    [Fact]
    public async Task An_interrupted_download_resumes_where_it_stopped()
    {
        await File.WriteAllBytesAsync(Part, Content[..40_000], Ct);
        var server = new FakeServer();

        await Create(server).DownloadAsync(Model, _directory, null, Ct);

        server.RangeStarts.ShouldBe([40_000]);
        (await File.ReadAllBytesAsync(Target, Ct)).ShouldBe(Content);
        File.Exists(Part).ShouldBeFalse();
    }

    [Fact]
    public async Task A_server_that_ignores_the_range_gives_the_whole_file_and_it_is_still_right()
    {
        await File.WriteAllBytesAsync(Part, Content[..40_000], Ct);
        var server = new FakeServer { IgnoreRange = true };

        await Create(server).DownloadAsync(Model, _directory, null, Ct);

        (await File.ReadAllBytesAsync(Target, Ct)).ShouldBe(Content);
    }

    [Fact]
    public async Task A_range_the_server_refuses_starts_over()
    {
        await File.WriteAllBytesAsync(Part, Content[..40_000], Ct);
        var server = new FakeServer { AlwaysRangeNotSatisfiable = true };

        await Create(server).DownloadAsync(Model, _directory, null, Ct);

        server.RangeStarts.ShouldBe([40_000, null]);
        (await File.ReadAllBytesAsync(Target, Ct)).ShouldBe(Content);
    }

    [Fact]
    public async Task A_partial_file_larger_than_the_model_is_thrown_away()
    {
        await File.WriteAllBytesAsync(Part, new byte[Content.Length + 10], Ct);

        await Create(new FakeServer()).DownloadAsync(Model, _directory, null, Ct);

        (await File.ReadAllBytesAsync(Target, Ct)).ShouldBe(Content);
    }

    [Fact]
    public async Task A_dropped_connection_keeps_what_came_so_that_the_next_try_resumes()
    {
        var server = new FakeServer { DropAfterBytes = 30_000 };

        await Should.ThrowAsync<IOException>(() => Create(server).DownloadAsync(Model, _directory, null, Ct));

        File.Exists(Target).ShouldBeFalse();
        new FileInfo(Part).Length.ShouldBe(30_000);

        server.DropAfterBytes = null;
        await Create(server).DownloadAsync(Model, _directory, null, Ct);

        server.RangeStarts.ShouldBe([null, 30_000]);
        (await File.ReadAllBytesAsync(Target, Ct)).ShouldBe(Content);
    }

    [Fact]
    public async Task A_file_that_does_not_match_the_hash_is_never_kept()
    {
        var wrong = new byte[Content.Length];
        Array.Fill(wrong, (byte)7);

        var error = await Should.ThrowAsync<ModelIntegrityException>(() => Create(new FakeServer { Body = wrong }).DownloadAsync(Model, _directory, null, Ct));

        error.Message.ShouldContain("model.bin");
        error.Message.ShouldContain(Model.Sha256);
        File.Exists(Target).ShouldBeFalse();
        File.Exists(Part).ShouldBeFalse("a corrupt file must not be resumed");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_server_error_says_which_status_and_which_file(HttpStatusCode status)
    {
        var error = await Should.ThrowAsync<HttpRequestException>(() => Create(new FakeServer { Status = status }).DownloadAsync(Model, _directory, null, Ct));

        error.Message.ShouldContain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
        error.Message.ShouldContain("model.bin");
        File.Exists(Target).ShouldBeFalse();
    }

    [Fact]
    public async Task Without_room_on_the_disk_it_stops_before_asking_for_anything()
    {
        var server = new FakeServer();

        var error = await Should.ThrowAsync<InsufficientDiskSpaceException>(() => Create(server, freeSpace: 1_000).DownloadAsync(Model, _directory, null, Ct));

        error.Message.ShouldContain("model.bin");
        server.RangeStarts.ShouldBeEmpty();
    }

    [Fact]
    public async Task What_was_already_downloaded_counts_as_room()
    {
        await File.WriteAllBytesAsync(Part, Content[..90_000], Ct);

        // Only 10 000 bytes are missing; the margin is the downloader's own and small for a test file.
        await new ModelDownloader(
            new HttpClient(new FakeServer()),
            new ModelDownloaderOptions { FreeSpace = _ => 10_000 + 1_000, SafetyMargin = 1_000 }).DownloadAsync(Model, _directory, null, Ct);

        File.Exists(Target).ShouldBeTrue();
    }

    [Fact]
    public async Task A_connection_that_stays_open_and_sends_nothing_is_given_up_with_a_clear_message()
    {
        var error = await Should.ThrowAsync<IOException>(() =>
            Create(new FakeServer { Stall = true }, stall: TimeSpan.FromMilliseconds(150)).DownloadAsync(Model, _directory, null, Ct));

        error.Message.ShouldContain("sem receber dados");
    }

    [Fact]
    public async Task Cancelling_stops_and_keeps_the_partial_file()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelAfterFirstReport(cancellation);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            new ModelDownloader(new HttpClient(new FakeServer { DropAfterBytes = null }), new ModelDownloaderOptions { FreeSpace = _ => long.MaxValue, ProgressInterval = TimeSpan.Zero })
                .DownloadAsync(Model, _directory, progress, cancellation.Token));

        File.Exists(Target).ShouldBeFalse();
        File.Exists(Part).ShouldBeTrue();
    }

    private sealed class CancelAfterFirstReport(CancellationTokenSource cancellation) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => cancellation.Cancel();
    }

    [Fact]
    public void A_file_is_installed_only_when_it_has_the_size_of_the_model()
    {
        Model.IsInstalledIn(_directory).ShouldBeFalse();

        File.WriteAllBytes(Target, Content[..10]);
        Model.IsInstalledIn(_directory).ShouldBeFalse();

        File.WriteAllBytes(Target, Content);
        Model.IsInstalledIn(_directory).ShouldBeTrue();
    }

    [Fact]
    public void The_catalog_has_the_three_files_with_the_hashes_checked_against_their_owners()
    {
        ModelCatalog.All.Select(f => f.FileName).ShouldBe(["ggml-small.bin", "kokoro-v1.0.onnx", "voices-v1.0.bin"]);
        ModelCatalog.All.ShouldAllBe(f => f.Sha256.Length == 64 && f.Sha256.All(Uri.IsHexDigit) && f.Size > 0 && f.Url.Scheme == "https");
        ModelCatalog.WhisperSmall.Sha256.ShouldBe("1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", "the ETag Hugging Face publishes for the file");
        ModelCatalog.KokoroModel.Size.ShouldBe(325_532_387);
        ModelCatalog.KokoroVoices.Size.ShouldBe(28_214_398);
    }

    [Fact]
    public void A_name_in_the_catalog_is_complete_only_with_its_size_and_any_other_file_only_has_to_exist()
    {
        var small = Path.Combine(_directory, "ggml-small.bin");
        File.WriteAllBytes(small, new byte[10]);
        var other = Path.Combine(_directory, "ggml-base.bin");
        File.WriteAllBytes(other, new byte[10]);

        ModelCatalog.IsPresent(small).ShouldBeFalse("a truncated copy of a catalog model");
        ModelCatalog.IsPresent(other).ShouldBeTrue("a model the user brought");
        ModelCatalog.IsPresent(Path.Combine(_directory, "nada.bin")).ShouldBeFalse();
    }
}
