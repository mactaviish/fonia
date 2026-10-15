using Fonia.Core.Airports;

namespace Fonia.App;

/// <summary>The last airport the user flew from, kept in a small file so that the next start can offer it. Any failure is "none".</summary>
internal sealed class LastAirportStore(string path)
{
    /// <summary>The default place: <c>%LOCALAPPDATA%\Fonia\last-airport.txt</c>.</summary>
    public static LastAirportStore Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fonia", "last-airport.txt"));

    public string? Read()
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } icao ? icao : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Remember(string icao)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, icao);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not remembering is not a failure: the next start just asks without a suggestion.
        }
    }
}

/// <summary>
/// What the program does when it is opened without arguments on a terminal, which is how most users start it: it asks for the airport,
/// offers the last one (or the one of the settings), and goes into the voice mode. With arguments, or without a terminal, nothing changes.
/// </summary>
internal static class StartupWizard
{
    private const int Attempts = 3;

    public static bool ShouldAsk(string[] args, bool terminal) => args.Length == 0 && terminal;

    /// <returns>The options of the voice mode at the airport the user chose, or null when they gave up.</returns>
    public static async Task<AppOptions?> RunAsync(TextReader input, TextWriter output, IAirportCatalog catalog, string? defaultAirport, Action<string> remember, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(remember);

        output.WriteLine("Fonia: conversa por voz com o controle de tráfego aéreo.");

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            await output.WriteAsync(defaultAirport is null
                ? "Aeroporto (código ICAO, por exemplo SBCT): "
                : $"Aeroporto (código ICAO) [{defaultAirport}]: ");

            var line = (await input.ReadLineAsync(ct))?.Trim();

            if (line is null || line.Equals("sair", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var chosen = line.Length == 0 ? defaultAirport : line;

            if (chosen is null)
            {
                continue;
            }

            if (catalog.TryGet(chosen, out var airport))
            {
                remember(airport.Ident);
                return new AppOptions(AtcAirport: airport.Ident, Voice: true);
            }

            output.WriteLine($"Aeroporto '{chosen}' não encontrado nos dados (use o código ICAO, por exemplo SBCT).");
        }

        return null;
    }
}
