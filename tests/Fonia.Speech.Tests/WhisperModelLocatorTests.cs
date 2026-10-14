namespace Fonia.Speech.Tests;

public sealed class WhisperModelLocatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fonia-models-" + Guid.NewGuid().ToString("N"));

    public WhisperModelLocatorTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void The_default_folder_is_under_the_local_application_data_of_the_user()
    {
        WhisperModelLocator.DefaultDirectory.ShouldStartWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        WhisperModelLocator.DefaultDirectory.ShouldEndWith(Path.Combine("Fonia", "models"));
    }

    [Fact]
    public void The_default_model_is_small()
    {
        WhisperModelLocator.DefaultFileName.ShouldBe("ggml-small.bin");
    }

    [Fact]
    public void It_finds_the_model_in_the_folder_given()
    {
        var model = Path.Combine(_directory, "ggml-small.bin");
        FakeModelFiles.Create(model);

        WhisperModelLocator.Resolve(_directory, null).ShouldBe(model);
    }

    [Fact]
    public void It_finds_another_model_by_file_name()
    {
        var model = Path.Combine(_directory, "ggml-base.bin");
        FakeModelFiles.Create(model);

        WhisperModelLocator.Resolve(_directory, "ggml-base.bin").ShouldBe(model);
    }

    [Fact]
    public void A_missing_model_explains_where_it_looked_and_where_to_get_it()
    {
        var error = Should.Throw<FileNotFoundException>(() => WhisperModelLocator.Resolve(_directory, null));

        error.Message.ShouldContain(Path.Combine(_directory, "ggml-small.bin"));
        error.Message.ShouldContain("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin");
        error.Message.ShouldContain("--stt-dir");
    }

    [Fact]
    public void A_file_name_with_a_path_is_refused_so_that_the_folder_stays_the_only_place_looked_in()
    {
        Should.Throw<ArgumentException>(() => WhisperModelLocator.Resolve(_directory, Path.Combine("..", "ggml-small.bin")));
    }

    [Fact]
    public void Without_a_folder_the_one_next_to_the_program_comes_first()
    {
        var bundled = Path.Combine(_directory, "bundled");
        var downloaded = Path.Combine(_directory, "downloaded");
        FakeModelFiles.Create(Path.Combine(bundled, "ggml-small.bin"));
        FakeModelFiles.Create(Path.Combine(downloaded, "ggml-small.bin"));

        WhisperModelLocator.Resolve(null, null, [bundled, downloaded]).ShouldBe(Path.Combine(bundled, "ggml-small.bin"));
    }

    [Fact]
    public void Without_a_folder_it_falls_back_to_the_next_one_that_has_the_model()
    {
        var bundled = Path.Combine(_directory, "bundled");
        var downloaded = Path.Combine(_directory, "downloaded");
        FakeModelFiles.Create(Path.Combine(downloaded, "ggml-small.bin"));

        WhisperModelLocator.Resolve(null, null, [bundled, downloaded]).ShouldBe(Path.Combine(downloaded, "ggml-small.bin"));
    }

    [Fact]
    public void A_folder_given_by_the_user_is_the_only_one_looked_in()
    {
        var bundled = Path.Combine(_directory, "bundled");
        var mine = Path.Combine(_directory, "mine");
        FakeModelFiles.Create(Path.Combine(bundled, "ggml-small.bin"));
        Directory.CreateDirectory(mine);

        Should.Throw<FileNotFoundException>(() => WhisperModelLocator.Resolve(mine, null, [bundled]));
    }

    [Fact]
    public void A_model_of_the_catalog_that_was_cut_short_is_not_taken_for_the_model()
    {
        var path = Path.Combine(_directory, "ggml-small.bin");
        File.WriteAllBytes(path, new byte[1000]);

        Should.Throw<FileNotFoundException>(() => WhisperModelLocator.Resolve(_directory, null));
    }

    [Fact]
    public void A_model_the_user_brought_only_has_to_exist()
    {
        var path = Path.Combine(_directory, "ggml-base.bin");
        File.WriteAllBytes(path, new byte[1000]);

        WhisperModelLocator.Resolve(_directory, "ggml-base.bin").ShouldBe(path);
    }

    [Fact]
    public void The_message_offers_the_download_command()
    {
        var error = Should.Throw<FileNotFoundException>(() => WhisperModelLocator.Resolve(_directory, null));

        error.Message.ShouldContain("--download-models");
    }

    [Fact]
    public void The_folders_searched_are_the_one_next_to_the_program_and_the_one_of_the_user()
    {
        ModelDirectories.Search.ShouldBe([Path.Combine(AppContext.BaseDirectory, "models"), ModelDirectories.Default]);
    }
}
