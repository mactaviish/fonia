#:project ../src/Fonia.Data/Fonia.Data.csproj

// Refreshes data/aisweb/br-aisweb-frequencies.csv from the AISWEB API of DECEA (area "rotaer"), with the key of the maintainer.
// The data belongs to DECEA and is used under its Politica de Uso (item 10: who has authorized access to the API may keep the data
// in its own database and distribute it); see docs/decisions/approach-frequencies.md and the NOTICE file.
//
//   $env:FONIA_AISWEB_KEY = "..."; $env:FONIA_AISWEB_PASS = "..."
//   dotnet run tools/update-aisweb.cs                          the aerodromes the app can use (see below)
//   dotnet run tools/update-aisweb.cs -- --icao SBXX,SBYY      only these
//   dotnet run tools/update-aisweb.cs -- --save-raw <folder>   also keeps each XML answer in <folder>/<ICAO>.xml
//   dotnet run tools/update-aisweb.cs -- --from <folder>       no network: reads <folder>/<ICAO>.xml saved before
//
// Which aerodromes: the ones of OurAirports (data/ourairports) with scheduled service, of type large or medium, with any
// frequency, or in data/atc-names.csv. One call per aerodrome, one second apart: the policy forbids heavy load (item 13).
// The key and the password go in the query string, so the URL is never printed or logged here.

using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Fonia.Data;
using Fonia.Data.Aisweb;

const string Endpoint = "https://aisweb.decea.gov.br/api/";

string[] columns = ["icao", "name", "city", "uf", "service", "callsign", "frequency_mhz", "note"];

var repository = Path.GetFullPath(Path.Combine(ScriptDirectory(), ".."));
var output = Path.Combine(repository, "data", "aisweb");
var from = Option("--from");
var saveRaw = Option("--save-raw");
var explicitIcao = Option("--icao")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var key = Environment.GetEnvironmentVariable("FONIA_AISWEB_KEY");
var pass = Environment.GetEnvironmentVariable("FONIA_AISWEB_PASS");

if (from is null && (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(pass)))
{
    Console.Error.WriteLine("Set FONIA_AISWEB_KEY and FONIA_AISWEB_PASS (the API-Key and API-Pass DECEA sent by e-mail).");
    return 1;
}

var candidates = (explicitIcao is not null ? [.. explicitIcao] : Candidates()).Select(i => i.ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal).ToList();
Console.WriteLine($"{candidates.Count} aerodromes.");

if (saveRaw is not null)
{
    Directory.CreateDirectory(saveRaw);
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var aerodromes = new List<AiswebAerodrome>();
var failures = new List<string>();

foreach (var icao in candidates)
{
    var xml = await Fetch(icao);

    if (xml is null)
    {
        continue;
    }

    try
    {
        var aerodrome = AiswebRotaer.Parse(xml);

        if (aerodrome is null)
        {
            failures.Add($"{icao}: no aerodrome in the answer");
        }
        else if (aerodrome.Services.Count > 0)
        {
            aerodromes.Add(aerodrome);
        }
    }
    catch (DataFormatException ex)
    {
        failures.Add($"{icao}: {ex.Message}");
    }
}

var lines = new List<string> { string.Join(',', columns) };

foreach (var aerodrome in aerodromes.OrderBy(a => a.Icao, StringComparer.Ordinal))
{
    foreach (var service in aerodrome.Services)
    {
        foreach (var frequency in service.Frequencies)
        {
            string[] fields =
            [
                aerodrome.Icao, aerodrome.Name, aerodrome.City, aerodrome.Uf, service.Type, service.Callsign,
                frequency.Mhz.ToString("0.000", CultureInfo.InvariantCulture), frequency.Note,
            ];

            lines.Add(string.Join(',', fields.Select(Quote)));
        }
    }
}

Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "br-aisweb-frequencies.csv"), string.Join('\n', lines) + "\n", new UTF8Encoding(false));

var dates = aerodromes.Select(a => a.UpdatedOn).Where(d => d.Length > 0).Order(StringComparer.Ordinal).ToList();

File.WriteAllText(
    Path.Combine(output, "snapshot.txt"),
    $"Source: AISWEB API (area rotaer), DECEA - https://aisweb.decea.gov.br/{Environment.NewLine}" +
    $"Retrieved: {DateTime.UtcNow:yyyy-MM-dd} (UTC){Environment.NewLine}" +
    $"Aerodromes asked: {candidates.Count}; with communication services: {aerodromes.Count}; frequencies: {lines.Count - 1}{Environment.NewLine}" +
    $"Update dates of the aerodromes: {(dates.Count > 0 ? dates[0] + " to " + dates[^1] : "none")}{Environment.NewLine}",
    new UTF8Encoding(false));

foreach (var failure in failures)
{
    Console.Error.WriteLine($"warning: {failure}");
}

Console.WriteLine($"Done: {aerodromes.Count} aerodromes with services, {lines.Count - 1} frequencies in {output}");
return failures.Count > candidates.Count / 10 ? 2 : 0;

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

async Task<string?> Fetch(string icao)
{
    if (from is not null)
    {
        var file = Path.Combine(from, icao + ".xml");
        return File.Exists(file) ? await File.ReadAllTextAsync(file, Encoding.UTF8) : null;
    }

    for (var attempt = 1; attempt <= 3; attempt++)
    {
        await Task.Delay(TimeSpan.FromSeconds(attempt == 1 ? 1 : 5 * attempt));

        try
        {
            var query = $"apiKey={Uri.EscapeDataString(key!)}&apiPass={Uri.EscapeDataString(pass!)}&area=rotaer&icaoCode={icao}";
            using var response = await http.GetAsync($"{Endpoint}?{query}");

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var text = await response.Content.ReadAsStringAsync();

                if (saveRaw is not null)
                {
                    await File.WriteAllTextAsync(Path.Combine(saveRaw, icao + ".xml"), text, new UTF8Encoding(false));
                }

                return text;
            }

            Console.Error.WriteLine($"{icao}: HTTP {(int)response.StatusCode} (attempt {attempt})");

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException("The API refused the key and password.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The message of these exceptions is not printed: it could carry the address with the credentials.
            Console.Error.WriteLine($"{icao}: {ex.GetType().Name} (attempt {attempt})");
        }
    }

    failures.Add($"{icao}: no answer");
    return null;
}

List<string> Candidates()
{
    var airports = ReadTable(Path.Combine(repository, "data", "ourairports", "br-airports.csv"));
    var withFrequency = ReadTable(Path.Combine(repository, "data", "ourairports", "br-airport-frequencies.csv"))
        .Select(r => r["airport_ident"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var named = ReadTable(Path.Combine(repository, "data", "atc-names.csv")).Select(r => r["icao"]).ToHashSet(StringComparer.OrdinalIgnoreCase);

    return airports
        .Where(r => r["scheduled_service"] == "yes"
            || r["type"] is "large_airport" or "medium_airport"
            || withFrequency.Contains(r["ident"])
            || named.Contains(r["ident"]))
        .Select(r => r["ident"])
        .Where(i => i.Length == 4 && i.All(char.IsAsciiLetterUpper))
        .ToList();
}

static IReadOnlyList<CsvRow> ReadTable(string path)
{
    using var stream = File.OpenRead(path);
    return CsvTable.Read(stream).Rows;
}

// Quote only when needed; LF line endings, so the file is the same on every system.
static string Quote(string field)
{
    return field.IndexOfAny([',', '"', '\r', '\n']) >= 0 || field != field.Trim()
        ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
        : field;
}

static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
