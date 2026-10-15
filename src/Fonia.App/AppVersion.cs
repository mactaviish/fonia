using System.Reflection;

namespace Fonia.App;

/// <summary>The version of the program, as set in <c>Directory.Build.props</c>.</summary>
internal static class AppVersion
{
    public static string Current { get; } = Of(typeof(AppVersion).Assembly);

    /// <summary>The informational version without the "+commit" the SDK appends: "0.1.0".</summary>
    internal static string Of(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        var plus = informational.IndexOf('+', StringComparison.Ordinal);

        return plus < 0 ? informational : informational[..plus];
    }
}
