using Fonia.Core.Phraseology;

namespace Fonia.App;

/// <summary>
/// The telephony table of this run: the one of the repository plus the companies the user added (a virtual airline), from
/// <c>--telephony</c> and the <c>telephony</c> setting.
/// </summary>
internal static class Companies
{
    public static TelephonyTable From(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var table = TelephonyTable.BuiltIn;

        foreach (var (designator, spoken) in options.Telephony ?? new Dictionary<string, string>())
        {
            table = table.With(designator, spoken);
        }

        return table;
    }
}
