using System.Text;
using Microsoft.Extensions.Configuration;

namespace Fonia.App;

/// <summary>A settings file the loader looked for, and whether it was there.</summary>
internal sealed record SettingsFile(string Path, bool Exists);

/// <summary>The settings the app found, the files it looked at, or what is wrong with them.</summary>
internal sealed record LoadedSettings(AppSettings Settings, IReadOnlyList<SettingsFile> Files, string? Error);

/// <summary>
/// Reads the settings in order of precedence: the file next to the program (machine or portable defaults), the file of the user, and the
/// <c>FONIA_*</c> environment variables ("FONIA_VOICE__PTTKEY" is voice:pttKey). Comments and trailing commas are allowed in the files,
/// since people edit them by hand. A key the program does not know, or a value of the wrong kind, is an error that names it: a typo
/// that is silently ignored leaves the user wondering why nothing changed.
/// </summary>
internal static class SettingsLoader
{
    public const string FileName = "appsettings.json";
    private const string EnvironmentPrefix = "FONIA_";

    /// <summary>The settings file of the user: <c>%APPDATA%\Fonia\appsettings.json</c>.</summary>
    public static string UserFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fonia", FileName);

    public static LoadedSettings Load(string baseDirectory, string userDirectory, IEnumerable<KeyValuePair<string, string?>> environment)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        ArgumentNullException.ThrowIfNull(userDirectory);
        ArgumentNullException.ThrowIfNull(environment);

        var files = new[] { Path.Combine(baseDirectory, FileName), Path.Combine(userDirectory, FileName) };
        var listed = files.Select(f => new SettingsFile(f, File.Exists(f))).ToList();
        var settings = new AppSettings();

        try
        {
            var builder = new ConfigurationBuilder();

            foreach (var file in files)
            {
                builder.AddJsonFile(file, optional: true, reloadOnChange: false);
            }

            builder.AddInMemoryCollection(EnvironmentSettings(environment));
            builder.Build().Bind(settings, binder => binder.ErrorOnUnknownConfiguration = true);

            return new LoadedSettings(settings, listed, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new LoadedSettings(new AppSettings(), listed, $"as configurações não puderam ser lidas ({FileName}): {Describe(ex)}");
        }
    }

    // The files are read by the provider, which names the file in its message; for the rest the message of the binder names the key.
    private static string Describe(Exception ex)
    {
        var message = new StringBuilder(ex.Message.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal));

        if (ex.InnerException is { } inner)
        {
            message.Append(" (").Append(inner.Message.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)).Append(')');
        }

        return message.ToString();
    }

    private static IEnumerable<KeyValuePair<string, string?>> EnvironmentSettings(IEnumerable<KeyValuePair<string, string?>> environment) =>
        environment
            .Where(pair => pair.Key.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(pair => new KeyValuePair<string, string?>(pair.Key[EnvironmentPrefix.Length..].Replace("__", ":", StringComparison.Ordinal), pair.Value));

    /// <summary>The process environment, in the form <see cref="Load"/> takes.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> ProcessEnvironment()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            yield return new KeyValuePair<string, string?>((string)entry.Key, entry.Value as string);
        }
    }
}
