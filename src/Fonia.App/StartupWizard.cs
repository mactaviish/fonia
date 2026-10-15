using Fonia.Core.Airports;

namespace Fonia.App;

/// <summary>One small answer of the user, kept in a file so that the next start can offer it. Any failure is "none".</summary>
internal sealed class LastValueStore(string path)
{
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fonia");

    /// <summary>The last airport: <c>%LOCALAPPDATA%\Fonia\last-airport.txt</c>.</summary>
    public static LastValueStore Airport { get; } = new(Path.Combine(Folder, "last-airport.txt"));

    /// <summary>The last callsign typed: <c>%LOCALAPPDATA%\Fonia\last-callsign.txt</c>.</summary>
    public static LastValueStore Callsign { get; } = new(Path.Combine(Folder, "last-callsign.txt"));

    public string? Read()
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } value ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Remember(string value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not remembering is not a failure: the next start just asks without a suggestion.
        }
    }
}

/// <summary>What the simulator says about the flight that is loaded: the registration of the aircraft and where it is.</summary>
internal sealed record SimulatorFix(string Registration, double LatitudeDegrees, double LongitudeDegrees);

/// <summary>What the start-up offers as the answer when the user only presses Enter.</summary>
internal sealed record WizardDefaults(string? Airport, string? Callsign);

/// <summary>
/// What the program does when it is opened without arguments on a terminal, which is how most users start it. It looks for the simulator:
/// if there is a flight loaded, the callsign is the registration of the aircraft and the airport is the one it is at, and nothing is
/// asked. What the simulator cannot give (it is not running, or the aircraft is far from any airport) is asked, offering the last answer.
/// Then it goes into the voice mode. With arguments, or without a terminal, nothing changes.
/// </summary>
internal static class StartupWizard
{
    private const int Attempts = 3;
    private const int MinimumCallsignLength = 3;
    private const int MaximumCallsignLength = 10;

    // The aircraft is "at" an airport if it is this near: on the apron or the runway, not flying over the countryside.
    private const double MostKilometersFromAirport = 15;

    public static bool ShouldAsk(string[] args, bool terminal) => args.Length == 0 && terminal;

    /// <summary>The callsign the way the engine reads it: letters and digits, in capitals ("pr-abc" is "PRABC"). Empty when it is not one.</summary>
    public static string NormalizeCallsign(string? text)
    {
        var letters = new string([.. (text ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

        return letters.Length is >= MinimumCallsignLength and <= MaximumCallsignLength ? letters : string.Empty;
    }

    /// <returns>The options of the voice mode at the airport and with the callsign, or null when the user gave up.</returns>
    public static async Task<AppOptions?> RunAsync(
        TextReader input,
        TextWriter output,
        IAirportCatalog catalog,
        WizardDefaults defaults,
        Func<CancellationToken, Task<SimulatorFix?>> readSimulator,
        Func<double, double, (Airport Airport, double Kilometers)?> nearestAirport,
        Action<string> rememberAirport,
        Action<string> rememberCallsign,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(readSimulator);
        ArgumentNullException.ThrowIfNull(nearestAirport);
        ArgumentNullException.ThrowIfNull(rememberAirport);
        ArgumentNullException.ThrowIfNull(rememberCallsign);

        output.WriteLine("Fonia: conversa por voz com o controle de tráfego aéreo.");
        output.WriteLine("Procurando o simulador...");

        var fix = await readSimulator(ct);
        string? callsign = null;
        string? airport = null;

        if (fix is null)
        {
            output.WriteLine("Simulador não encontrado (ou sem voo carregado): informe o aeroporto e o indicativo.");
        }
        else
        {
            callsign = NormalizeCallsign(fix.Registration).Length > 0 ? fix.Registration.Trim() : null;
            output.WriteLine(callsign is null ? "Simulador encontrado, mas a aeronave não tem indicativo." : $"Simulador encontrado: aeronave {callsign}.");

            if (nearestAirport(fix.LatitudeDegrees, fix.LongitudeDegrees) is { } near && near.Kilometers <= MostKilometersFromAirport)
            {
                airport = near.Airport.Ident;
                output.WriteLine($"Aeroporto: {near.Airport.Ident} {near.Airport.Name} (a {near.Kilometers:0.#} km da aeronave).");
            }
            else
            {
                output.WriteLine("Simulador encontrado, mas nenhum aeroporto por perto: informe o aeroporto.");
            }
        }

        airport ??= await AskAirportAsync(input, output, catalog, defaults.Airport, ct);

        if (airport is null)
        {
            return null;
        }

        callsign ??= await AskCallsignAsync(input, output, defaults.Callsign, ct);

        if (callsign is null)
        {
            return null;
        }

        rememberAirport(airport);
        rememberCallsign(callsign);

        return new AppOptions(AtcAirport: airport, Voice: true, Callsign: callsign);
    }

    private static async Task<string?> AskAirportAsync(TextReader input, TextWriter output, IAirportCatalog catalog, string? defaultAirport, CancellationToken ct)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var line = await AskAsync(input, output, defaultAirport is null ? "Aeroporto (código ICAO, por exemplo SBCT): " : $"Aeroporto (código ICAO) [{defaultAirport}]: ", ct);

            if (line is null)
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
                return airport.Ident;
            }

            output.WriteLine($"Aeroporto '{chosen}' não encontrado nos dados (use o código ICAO, por exemplo SBCT).");
        }

        return null;
    }

    private static async Task<string?> AskCallsignAsync(TextReader input, TextWriter output, string? defaultCallsign, CancellationToken ct)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var line = await AskAsync(input, output, defaultCallsign is null ? "Indicativo (matrícula da aeronave, por exemplo PR-ABC): " : $"Indicativo (matrícula) [{defaultCallsign}]: ", ct);

            if (line is null)
            {
                return null;
            }

            var chosen = line.Length == 0 ? defaultCallsign : line;

            if (chosen is null)
            {
                continue;
            }

            if (NormalizeCallsign(chosen).Length > 0)
            {
                return chosen;
            }

            output.WriteLine($"Indicativo '{chosen}' inválido: use de {MinimumCallsignLength} a {MaximumCallsignLength} letras e números, por exemplo PR-ABC.");
        }

        return null;
    }

    // The trimmed answer ("" for just Enter), or null when the user wants out ("sair") or there is no more input.
    private static async Task<string?> AskAsync(TextReader input, TextWriter output, string question, CancellationToken ct)
    {
        await output.WriteAsync(question);
        var line = (await input.ReadLineAsync(ct))?.Trim();

        return line is null || line.Equals("sair", StringComparison.OrdinalIgnoreCase) ? null : line;
    }
}
