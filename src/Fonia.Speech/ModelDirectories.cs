namespace Fonia.Speech;

/// <summary>Where the app looks for the models, in order, when the user did not say a folder.</summary>
public static class ModelDirectories
{
    /// <summary><c>models</c> next to the program: where a distribution that brings the models puts them.</summary>
    public static string Bundled { get; } = Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary><c>%LOCALAPPDATA%\Fonia\models</c> on Windows: where the models are downloaded to.</summary>
    public static string Default { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fonia", "models");

    /// <summary>The folders searched, first the one next to the program.</summary>
    public static IReadOnlyList<string> Search { get; } = [Bundled, Default];
}
