#:project ../src/Fonia.Data/Fonia.Data.csproj

// Refreshes data/ourairports/br-*.csv from the OurAirports open data (public domain, https://ourairports.com/data/).
//
//   dotnet run tools/update-ourairports.cs                      downloads the three files from GitHub
//   dotnet run tools/update-ourairports.cs -- --from <folder>   uses airports.csv, runways.csv and
//                                                               airport-frequencies.csv already downloaded
//
// It keeps only what the app uses: the airports of Brazil of type large, medium or small (no heliports, no closed
// aerodromes), and the runways and frequencies of those airports, with only the columns the loader reads.
// Output is sorted by ident so that a refresh shows up in git as a readable diff.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Fonia.Data;

const string SourceUrl = "https://raw.githubusercontent.com/davidmegginson/ourairports-data/main";

string[] airportColumns =
[
    "id", "ident", "type", "name", "latitude_deg", "longitude_deg", "elevation_ft",
    "iso_region", "municipality", "scheduled_service", "icao_code", "iata_code", "gps_code", "local_code",
];

string[] runwayColumns =
[
    "airport_ident", "length_ft", "width_ft", "surface", "lighted", "closed",
    "le_ident", "le_heading_degT", "le_elevation_ft", "he_ident", "he_heading_degT", "he_elevation_ft",
];

string[] frequencyColumns = ["airport_ident", "type", "description", "frequency_mhz"];

HashSet<string> keptTypes = ["large_airport", "medium_airport", "small_airport"];

var repository = Path.GetFullPath(Path.Combine(ScriptDirectory(), ".."));
var output = Path.Combine(repository, "data", "ourairports");
Directory.CreateDirectory(output);

var fromIndex = Array.IndexOf(args, "--from");
var from = fromIndex >= 0 && fromIndex + 1 < args.Length ? args[fromIndex + 1] : null;

using var http = new HttpClient();

async Task<CsvTable> ReadSource(string name)
{
    if (from is not null)
    {
        await using var file = File.OpenRead(Path.Combine(from, name));
        return CsvTable.Read(file);
    }

    Console.WriteLine($"Downloading {name} ...");
    await using var stream = await http.GetStreamAsync($"{SourceUrl}/{name}");
    return CsvTable.Read(stream);
}

var airports = (await ReadSource("airports.csv")).Rows
    .Where(r => r["iso_country"] == "BR" && keptTypes.Contains(r["type"]))
    .OrderBy(r => r["ident"], StringComparer.Ordinal)
    .ToList();
var idents = airports.Select(r => r["ident"]).ToHashSet(StringComparer.Ordinal);

var runways = (await ReadSource("runways.csv")).Rows
    .Where(r => idents.Contains(r["airport_ident"]))
    .OrderBy(r => r["airport_ident"], StringComparer.Ordinal)
    .ToList();

var frequencies = (await ReadSource("airport-frequencies.csv")).Rows
    .Where(r => idents.Contains(r["airport_ident"]))
    .OrderBy(r => r["airport_ident"], StringComparer.Ordinal)
    .ToList();

Write("br-airports.csv", airportColumns, airports);
Write("br-runways.csv", runwayColumns, runways);
Write("br-airport-frequencies.csv", frequencyColumns, frequencies);

File.WriteAllText(
    Path.Combine(output, "snapshot.txt"),
    $"Source: {SourceUrl} (OurAirports, public domain){Environment.NewLine}" +
    $"Retrieved: {DateTime.UtcNow:yyyy-MM-dd} (UTC){Environment.NewLine}" +
    $"Filter: iso_country BR; airport types large, medium, small{Environment.NewLine}" +
    $"Airports: {airports.Count}; runways: {runways.Count}; frequencies: {frequencies.Count}{Environment.NewLine}",
    new UTF8Encoding(false));

Console.WriteLine($"Done: {airports.Count} airports, {runways.Count} runways, {frequencies.Count} frequencies in {output}");
return 0;

void Write(string file, string[] columns, List<CsvRow> rows)
{
    var text = new StringBuilder();
    text.Append(string.Join(',', columns)).Append('\n');

    foreach (var row in rows)
    {
        text.Append(string.Join(',', columns.Select(c => Quote(row[c])))).Append('\n');
    }

    File.WriteAllText(Path.Combine(output, file), text.ToString(), new UTF8Encoding(false));
}

// Quote only when needed; LF line endings, so the files are the same on every system.
static string Quote(string field)
{
    return field.IndexOfAny([',', '"', '\r', '\n']) >= 0 || field != field.Trim()
        ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
        : field;
}

static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
