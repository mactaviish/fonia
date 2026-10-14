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
        File.WriteAllText(model, "x");

        WhisperModelLocator.Resolve(_directory, null).ShouldBe(model);
    }

    [Fact]
    public void It_finds_another_model_by_file_name()
    {
        var model = Path.Combine(_directory, "ggml-base.bin");
        File.WriteAllText(model, "x");

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
}
