using System.Text;
using Fonia.Speech;

namespace Fonia.App.Tests;

public sealed class ModelProvisionerTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fonia-provision-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _output = new(new StringBuilder()) { NewLine = "\n" };
    private readonly StringWriter _error = new(new StringBuilder()) { NewLine = "\n" };

    public ModelProvisionerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeDownloader : IModelDownloader
    {
        public List<(string File, string Directory)> Calls { get; } = [];

        public Func<ModelFile, Exception?>? Fails { get; set; }

        public Task DownloadAsync(ModelFile file, string directory, IProgress<DownloadProgress>? progress, CancellationToken ct)
        {
            Calls.Add((file.FileName, directory));

            if (Fails?.Invoke(file) is { } failure)
            {
                throw failure;
            }

            progress?.Report(new DownloadProgress(file.FileName, file.Size, file.Size, 1_000_000));
            return Task.CompletedTask;
        }
    }

    private static void Install(string directory, ModelFile file)
    {
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, file.FileName));
        stream.SetLength(file.Size);
    }

    private ModelProvisioner Create(FakeDownloader downloader, string typed = "", bool interactive = true, params string[] search) =>
        new(downloader, new StringReader(typed), _output, _error, interactive, search.Length == 0 ? [_directory] : search);

    [Fact]
    public async Task When_nothing_is_missing_nothing_is_asked_or_downloaded()
    {
        Install(_directory, ModelCatalog.WhisperSmall);
        var downloader = new FakeDownloader();

        var ok = await Create(downloader).EnsureAsync([new ModelNeed(ModelCatalog.WhisperSmall, null)], ask: true, Ct);

        ok.ShouldBeTrue();
        downloader.Calls.ShouldBeEmpty();
        _output.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_models_are_listed_with_what_they_are_for_and_how_big_and_the_user_is_asked()
    {
        var downloader = new FakeDownloader();

        await Create(downloader, "s\n").EnsureAsync([new ModelNeed(ModelCatalog.WhisperSmall, null), new ModelNeed(ModelCatalog.KokoroModel, null)], ask: true, Ct);

        var text = _output.ToString();
        text.ShouldContain("ggml-small.bin");
        text.ShouldContain("reconhecimento de voz");
        text.ShouldContain("465,0 MB");
        text.ShouldContain("kokoro-v1.0.onnx");
        text.ShouldContain("Baixar agora");
        text.ShouldContain("[S/n]");
    }

    [Theory]
    [InlineData("s\n")]
    [InlineData("S\n")]
    [InlineData("sim\n")]
    [InlineData("y\n")]
    [InlineData("\n")]
    [InlineData("")]
    public async Task Yes_and_a_bare_enter_download_everything_that_is_missing_into_the_default_folder(string answer)
    {
        var downloader = new FakeDownloader();

        var ok = await Create(downloader, answer).EnsureAsync([new ModelNeed(ModelCatalog.WhisperSmall, null), new ModelNeed(ModelCatalog.KokoroVoices, null)], ask: true, Ct);

        ok.ShouldBeTrue();
        downloader.Calls.ShouldBe([("ggml-small.bin", ModelDirectories.Default), ("voices-v1.0.bin", ModelDirectories.Default)]);
        _output.ToString().ShouldContain("Modelos prontos");
    }

    [Theory]
    [InlineData("n\n")]
    [InlineData("N\n")]
    [InlineData("não\n")]
    [InlineData("talvez\n")]
    public async Task Anything_but_yes_downloads_nothing_and_says_how_to_do_it_later(string answer)
    {
        var downloader = new FakeDownloader();

        var ok = await Create(downloader, answer).EnsureAsync([new ModelNeed(ModelCatalog.WhisperSmall, null)], ask: true, Ct);

        ok.ShouldBeFalse();
        downloader.Calls.ShouldBeEmpty();
        _output.ToString().ShouldContain("--download-models");
    }

    [Fact]
    public async Task Without_a_terminal_it_does_not_ask_and_does_not_download_it_only_says_what_to_do()
    {
        var downloader = new FakeDownloader();

        var ok = await Create(downloader, "s\n", interactive: false).EnsureAsync([new ModelNeed(ModelCatalog.WhisperSmall, null)], ask: true, Ct);

        ok.ShouldBeFalse();
        downloader.Calls.ShouldBeEmpty();
        _error.ToString().ShouldContain("--download-models");
        _output.ToString().ShouldNotContain("[S/n]");
    }

    [Fact]
    public async Task The_explicit_command_downloads_without_asking()
    {
        var downloader = new FakeDownloader();

        var ok = await Create(downloader, interactive: false).EnsureAsync([new ModelNeed(ModelCatalog.WhisperSmall, null)], ask: false, Ct);

        ok.ShouldBeTrue();
        downloader.Calls.ShouldHaveSingleItem();
        _output.ToString().ShouldNotContain("[S/n]");
    }

    [Fact]
    public async Task Only_what_is_missing_is_downloaded()
    {
        Install(_directory, ModelCatalog.KokoroModel);
        var downloader = new FakeDownloader();

        await Create(downloader, "s\n").EnsureAsync(
            [new ModelNeed(ModelCatalog.KokoroModel, null), new ModelNeed(ModelCatalog.KokoroVoices, null)],
            ask: true,
            Ct);

        downloader.Calls.Select(c => c.File).ShouldBe(["voices-v1.0.bin"]);
    }

    [Fact]
    public async Task A_model_in_any_of_the_searched_folders_counts_as_there()
    {
        var bundled = Path.Combine(_directory, "bundled");
        var downloaded = Path.Combine(_directory, "downloaded");
        Install(bundled, ModelCatalog.WhisperSmall);
        var downloader = new FakeDownloader();

        var ok = await Create(downloader, "", true, bundled, downloaded).EnsureAsync([new ModelNeed(ModelCatalog.WhisperSmall, null)], ask: true, Ct);

        ok.ShouldBeTrue();
        downloader.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_folder_the_user_named_is_where_it_looks_and_where_it_downloads()
    {
        var mine = Path.Combine(_directory, "mine");
        Install(_directory, ModelCatalog.WhisperSmall); // in a searched folder, but the user said another one
        var downloader = new FakeDownloader();

        await Create(downloader, "s\n").EnsureAsync([new ModelNeed(ModelCatalog.WhisperSmall, mine)], ask: true, Ct);

        downloader.Calls.ShouldBe([("ggml-small.bin", mine)]);
    }

    [Fact]
    public async Task A_truncated_copy_counts_as_missing()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "ggml-small.bin"), new byte[1000], Ct);
        var downloader = new FakeDownloader();

        await Create(downloader, "s\n").EnsureAsync([new ModelNeed(ModelCatalog.WhisperSmall, null)], ask: true, Ct);

        downloader.Calls.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_failure_is_explained_and_stops_the_rest()
    {
        var downloader = new FakeDownloader { Fails = file => new HttpRequestException($"O servidor respondeu 404 ao baixar {file.FileName}.") };

        var ok = await Create(downloader, "s\n").EnsureAsync([new ModelNeed(ModelCatalog.WhisperSmall, null), new ModelNeed(ModelCatalog.KokoroModel, null)], ask: true, Ct);

        ok.ShouldBeFalse();
        downloader.Calls.Count.ShouldBe(1);
        _error.ToString().ShouldContain("404");
        _error.ToString().ShouldContain("--download-models");
    }

    [Theory]
    [InlineData(typeof(ModelIntegrityException))]
    [InlineData(typeof(InsufficientDiskSpaceException))]
    [InlineData(typeof(IOException))]
    public async Task Integrity_disk_and_connection_failures_are_reported_not_thrown(Type failure)
    {
        var downloader = new FakeDownloader { Fails = _ => (Exception)Activator.CreateInstance(failure, "o motivo")! };

        var ok = await Create(downloader, "s\n").EnsureAsync([new ModelNeed(ModelCatalog.WhisperSmall, null)], ask: true, Ct);

        ok.ShouldBeFalse();
        _error.ToString().ShouldContain("o motivo");
    }

    [Fact]
    public async Task Cancelling_the_download_says_that_what_came_is_kept()
    {
        var downloader = new FakeDownloader { Fails = _ => new OperationCanceledException() };

        var ok = await Create(downloader, "s\n").EnsureAsync([new ModelNeed(ModelCatalog.WhisperSmall, null)], ask: true, Ct);

        ok.ShouldBeFalse();
        _output.ToString().ShouldContain("continua de onde parou");
    }

    [Fact]
    public void The_needs_of_each_mode_are_the_models_that_mode_uses()
    {
        var voice = ModelNeeds.ForVoice(new AppOptions(SttDirectory: "S", TtsDirectory: "T"));

        voice.ShouldBe(
        [
            new ModelNeed(ModelCatalog.WhisperSmall, "S"),
            new ModelNeed(ModelCatalog.KokoroModel, "T"),
            new ModelNeed(ModelCatalog.KokoroVoices, "T"),
        ]);
        ModelNeeds.ForTranscribe(new AppOptions(SttDirectory: "S")).ShouldBe([new ModelNeed(ModelCatalog.WhisperSmall, "S")]);
        ModelNeeds.ForSpeak(new AppOptions()).ShouldBe([new ModelNeed(ModelCatalog.KokoroModel, null), new ModelNeed(ModelCatalog.KokoroVoices, null)]);
    }

    [Fact]
    public void A_recognizer_model_the_user_chose_is_not_downloaded()
    {
        var needs = ModelNeeds.ForVoice(new AppOptions(SttModel: "ggml-base.bin"));

        needs.Select(n => n.File.FileName).ShouldBe(["kokoro-v1.0.onnx", "voices-v1.0.bin"]);
        ModelNeeds.ForTranscribe(new AppOptions(SttModel: "ggml-base.bin")).ShouldBeEmpty();
        ModelNeeds.ForTranscribe(new AppOptions(SttModel: "GGML-SMALL.BIN")).ShouldHaveSingleItem();
    }

    [Fact]
    public void Everything_is_what_the_download_command_fetches()
    {
        ModelNeeds.All("X").ShouldBe(ModelCatalog.All.Select(f => new ModelNeed(f, "X")));
    }

    [Fact]
    public void The_progress_line_shows_how_much_how_fast_and_the_percentage()
    {
        DownloadProgressPrinter.Format(new DownloadProgress("ggml-small.bin", 243_801_000, 487_601_967, 8_600_000))
            .ShouldBe("  ggml-small.bin  232,5 de 465,0 MB (50%)  8,2 MB/s");
    }

    [Fact]
    public void Without_a_terminal_the_progress_is_one_line_for_every_tenth()
    {
        var printer = new DownloadProgressPrinter(_output, overwrite: false);

        for (var received = 0L; received <= 100; received += 5)
        {
            printer.Report(new DownloadProgress("m.bin", received * 1_000_000, 100_000_000, 1_000_000));
        }

        _output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBeInRange(10, 12);
    }

    [Fact]
    public void On_a_terminal_the_line_is_rewritten_and_ends_with_a_new_line()
    {
        var printer = new DownloadProgressPrinter(_output, overwrite: true);

        printer.Report(new DownloadProgress("m.bin", 10, 100, 1));
        printer.Report(new DownloadProgress("m.bin", 100, 100, 1));

        var text = _output.ToString();
        text.Count(c => c == '\r').ShouldBe(2);
        text.ShouldEndWith("\n");
    }
}
