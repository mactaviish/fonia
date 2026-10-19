using System.Globalization;
using Fonia.Core.Airports;
using Fonia.Sim.Msfs;

namespace Fonia.App;

/// <summary>Where the dump gets what the simulator knows about an airport: MSFS in the app, a fake in the tests.</summary>
internal interface IFacilitySource
{
    /// <summary>True for MSFS 2024, false for 2020, null when not known (valid after a read).</summary>
    bool? IsMsfs2024 { get; }

    /// <exception cref="FacilityReadException">The simulator cannot be reached.</exception>
    Task<AirportFacility> ReadAsync(string icao, CancellationToken ct);
}

internal sealed class MsfsFacilitySource : IFacilitySource
{
    private readonly MsfsFacilityReader _reader = new();

    public bool? IsMsfs2024 => _reader.IsMsfs2024;

    public Task<AirportFacility> ReadAsync(string icao, CancellationToken ct) => _reader.ReadAsync(icao, ct);
}

/// <summary>
/// <c>fonia --dump-layout &lt;ICAO&gt;</c>: asks MSFS what it knows about an airport, saves the answer as it came and shows what it found. The
/// spike of the airport map (ROADMAP M6, L0): its purpose is to see the real data before anything is built on it. Read only; nothing is sent.
/// </summary>
internal static class LayoutCommands
{
    /// <summary>Where the dumps go: <c>%LOCALAPPDATA%\Fonia\layouts</c>.</summary>
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fonia", "layouts");

    /// <returns>0 when the simulator gave the airport and at least one runway; 1 when it did not, or could not be reached.</returns>
    public static async Task<int> DumpAsync(
        string icao,
        IFacilitySource source,
        IAirportCatalog catalog,
        string root,
        TimeProvider time,
        TextWriter output,
        TextWriter error,
        CancellationToken ct)
    {
        output.WriteLine($"Pedindo ao MSFS os dados de {icao}. O simulador precisa estar aberto, com um voo carregado (pode ser em outro aeroporto).");

        AirportFacility facility;

        try
        {
            facility = await source.ReadAsync(icao, ct);
        }
        catch (FacilityReadException ex)
        {
            error.WriteLine($"fonia: não consegui falar com o MSFS: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("fonia: cancelado.");
            return 1;
        }

        var path = Path.Combine(root, "raw", $"{icao}.raw.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, AirportFacilityJson.Write(facility, new FacilityCapture($"fonia {AppVersion.Current}", time.GetUtcNow(), source.IsMsfs2024)), ct);
        }
        catch (IOException ex)
        {
            error.WriteLine($"fonia: não consegui gravar {path}: {ex.Message}");
            return 1;
        }

        var conversion = AirportLayoutConverter.Convert(facility);
        var layoutPath = Path.Combine(root, $"{icao}.json");

        try
        {
            if (conversion.Layout is { } layout)
            {
                await File.WriteAllTextAsync(layoutPath, AirportLayoutJson.Write(layout), ct);
            }
        }
        catch (IOException ex)
        {
            error.WriteLine($"fonia: não consegui gravar {layoutPath}: {ex.Message}");
            return 1;
        }

        foreach (var line in LayoutReport.Lines(icao, facility, source.IsMsfs2024, catalog, conversion))
        {
            output.WriteLine(line);
        }

        output.WriteLine();
        output.WriteLine($"Gravado em {path}");

        if (conversion.Layout is not null)
        {
            output.WriteLine($"Mapa gravado em {layoutPath}");
        }

        output.WriteLine("Traga este texto e o arquivo .raw.json; eles ficam só no seu computador.");

        return facility.Airport is not null && facility.Runways.Count > 0 ? 0 : 1;
    }
}

/// <summary>What the dump prints: the counts, how each request went, the frequencies against the airport data, and the check of the axes.</summary>
internal static class LayoutReport
{
    public static IReadOnlyList<string> Lines(string icao, AirportFacility facility, bool? isMsfs2024, IAirportCatalog catalog, LayoutConversion conversion)
    {
        var lines = new List<string>
        {
            string.Empty,
            $"Simulador: {(isMsfs2024 is { } is2024 ? (is2024 ? "MSFS 2024" : "MSFS 2020") : "versão desconhecida")}. Tempo total: {facility.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s.",
        };

        lines.AddRange(Counts(facility));
        lines.AddRange(Problems(facility));
        lines.Add(string.Empty);
        lines.AddRange(Frequencies(icao, facility, catalog));
        lines.Add(string.Empty);
        lines.AddRange(Axes(facility));
        lines.Add(string.Empty);
        lines.AddRange(Map(conversion));

        return lines;
    }

    // The layout made from the data, what was left out, and a test of the router over it: a route from every parking spot to the holding point
    // of every runway end, to see how often the map can speak a route.
    private static IEnumerable<string> Map(LayoutConversion conversion)
    {
        if (conversion.Layout is not { } layout)
        {
            yield return "Mapa: não deu para converter os dados em um mapa do aeródromo.";

            foreach (var note in conversion.Notes)
            {
                yield return $"  - {note}";
            }

            yield break;
        }

        var holding = layout.Nodes.Where(n => n.Kind == TaxiNodeKind.HoldShort).ToList();

        yield return $"Mapa convertido: {layout.Nodes.Count} nós e {layout.Edges.Count} arestas ({layout.Edges.Count(e => e.Kind == TaxiEdgeKind.Taxiway)} de taxiway, {layout.Edges.Count(e => e.Kind == TaxiEdgeKind.Runway)} de pista, {layout.Edges.Count(e => e.Kind == TaxiEdgeKind.Parking)} de estacionamento); {holding.Count} pontos de espera, {holding.Count(n => n.Runway is not null)} ligados a uma pista.";

        foreach (var note in conversion.Notes)
        {
            yield return $"  - {note}";
        }

        foreach (var line in RouteTest(layout))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> RouteTest(AirportLayout layout)
    {
        var parkings = layout.Parkings.Where(p => p.Node is not null).ToList();

        if (parkings.Count == 0)
        {
            yield return "Teste de rotas: sem estacionamento ligado ao grafo.";
            yield break;
        }

        yield return $"Teste de rotas (de cada um dos {parkings.Count} estacionamentos ligados até o ponto de espera de cada cabeceira):";

        foreach (var end in layout.Runways.SelectMany(r => new[] { r.Low, r.High }))
        {
            var results = parkings.Select(p => TaxiRouter.Find(layout, p.Position, end.Designator)).ToList();
            var found = results.OfType<TaxiRouteFound>().ToList();
            var refused = results.OfType<NoTaxiRoute>().GroupBy(r => r.Reason).OrderByDescending(g => g.Count()).ToList();

            yield return $"  cabeceira {end.Designator}: {found.Count} de {results.Count} com rota ({found.Count(f => f.Route.Crossings.Count > 0)} atravessando pista)"
                + (refused.Count == 0 ? string.Empty : $"; sem rota: {string.Join(", ", refused.Select(g => $"{ReasonText(g.Key)} {g.Count()}"))}");
        }
    }

    private static string ReasonText(NoRouteReason reason) => reason switch
    {
        NoRouteReason.NoTaxiways => "sem taxiways",
        NoRouteReason.UnknownRunway => "pista desconhecida",
        NoRouteReason.AircraftOffTheGraph => "longe do grafo",
        NoRouteReason.NoHoldingPoint => "sem ponto de espera",
        NoRouteReason.NotConnected => "grafo cortado",
        NoRouteReason.UnnamedTaxiway => "taxiway sem nome",
        _ => reason.ToString(),
    };

    private static IEnumerable<string> Counts(AirportFacility facility)
    {
        if (facility.Airport is not { } airport)
        {
            yield return "O simulador não mandou o aeroporto: o código não existe nele, ou ele recusou o pedido (veja os problemas abaixo).";
            yield break;
        }

        var inv = CultureInfo.InvariantCulture;

        yield return $"Aeroporto: {airport.Icao} ({airport.Region}) - {airport.Name}. Referência: {airport.LatitudeDegrees.ToString("0.0000", inv)}, {airport.LongitudeDegrees.ToString("0.0000", inv)}.";
        yield return $"  Pistas:           recebi {facility.Runways.Count} (o simulador diz que tem {airport.RunwayCount})";
        yield return $"  Frequências:      recebi {facility.Frequencies.Count} (diz {airport.FrequencyCount})";
        yield return $"  Pontos de táxi:   recebi {facility.TaxiPoints.Count} (diz {airport.TaxiPointCount}), dos quais {facility.TaxiPoints.Count(p => p.Type is 2 or 4 or 5 or 6)} de espera (hold short)";
        yield return $"  Caminhos:         recebi {facility.TaxiPaths.Count} (diz {airport.TaxiPathCount}): {string.Join(", ", facility.TaxiPaths.GroupBy(p => p.Type).OrderBy(g => g.Key).Select(g => $"{g.Count()} {PathTypeName(g.Key)}"))}";
        yield return $"  Nomes de taxiway: recebi {facility.TaxiNames.Count} (diz {airport.TaxiNameCount})" + (facility.TaxiNames.Count == 0 ? string.Empty : $": {string.Join(", ", facility.TaxiNames.Select(n => n.Name).Where(n => n.Length > 0).Distinct().Take(30))}");
        yield return $"  Estacionamentos:  recebi {facility.Parkings.Count} (diz {airport.TaxiParkingCount})";

        // The taxiways are the paths of type 1 (taxi) and 4 (path): which of them the simulator names is what a route can say.
        var taxiways = facility.TaxiPaths.Where(p => p.Type is 1 or 4).ToList();
        var byName = taxiways
            .Select(p => p.NameIndex < facility.TaxiNames.Count ? facility.TaxiNames[(int)p.NameIndex].Name : string.Empty)
            .Where(n => n.Length > 0)
            .GroupBy(n => n)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        if (taxiways.Count > 0)
        {
            yield return $"  Caminhos de táxi (tipos 1 e 4) com nome: {byName.Sum(g => g.Count())} de {taxiways.Count}" + (byName.Count == 0 ? string.Empty : $" ({string.Join(", ", byName.Take(30).Select(g => $"{g.Key}: {g.Count()}"))})");
        }
    }

    // The SDK's names for the type of a path.
    private static string PathTypeName(int type) => type switch
    {
        1 => "táxi (tipo 1)",
        2 => "pista",
        3 => "estacionamento",
        4 => "caminho (tipo 4)",
        5 => "fechado",
        6 => "veículo",
        7 => "estrada",
        8 => "linha pintada",
        _ => $"tipo {type}",
    };

    private static IEnumerable<string> Problems(AirportFacility facility)
    {
        foreach (var request in facility.Requests.Where(r => !r.Ended || r.Problems.Count > 0))
        {
            yield return $"  ! {request.Name}: {string.Join("; ", request.Problems)}{(request.Ended ? string.Empty : " (sem fim de dados)")}";
        }
    }

    private static IEnumerable<string> Frequencies(string icao, AirportFacility facility, IAirportCatalog catalog)
    {
        if (!catalog.TryGet(icao, out var airport))
        {
            yield return $"Frequências: {icao} não está no OurAirports; nada a comparar.";

            foreach (var f in facility.Frequencies)
            {
                yield return $"  simulador: {f.TypeName,-10} {Mhz(f.KiloHertz)}  {f.Name}";
            }

            yield break;
        }

        yield return $"Frequências: simulador x OurAirports ({icao})";

        foreach (var f in facility.Frequencies)
        {
            var match = airport.Frequencies.FirstOrDefault(o => o.Frequency.KHz == f.KiloHertz);
            var verdict = match is null
                ? "só no simulador"
                : TypeMatches(f.Type, match.Type) ? $"igual no OurAirports ({match.Type})" : $"mesma frequência, tipo diferente no OurAirports ({match.Type})";

            yield return $"  simulador: {f.TypeName,-10} {Mhz(f.KiloHertz)}  {f.Name,-20} {verdict}";
        }

        foreach (var o in airport.Frequencies.Where(o => facility.Frequencies.All(f => f.KiloHertz != o.Frequency.KHz)))
        {
            yield return $"  OurAirports: {o.Type,-10} {Mhz(o.Frequency.KHz)}  {o.Description,-20} só no OurAirports";
        }
    }

    // The sim's types against ours: Controle is the approach and the departure; Tráfego is the clearance delivery.
    private static bool TypeMatches(int simType, AirportFrequencyType ours) => (simType, ours) switch
    {
        (5, AirportFrequencyType.Ground) => true,
        (6, AirportFrequencyType.Tower) => true,
        (8 or 9, AirportFrequencyType.Approach) => true,
        (1, AirportFrequencyType.Atis) => true,
        (7 or 14 or 15, AirportFrequencyType.Delivery) => true,
        _ => false,
    };

    private static IEnumerable<string> Axes(AirportFacility facility)
    {
        if (FacilityAxisCheck.Evaluate(facility) is not { } check)
        {
            yield return "Eixos dos pontos de táxi: não deu para conferir (faltam o aeroporto, a pista ou os caminhos de pista).";
            yield break;
        }

        var inv = CultureInfo.InvariantCulture;

        yield return $"Eixos dos pontos de táxi (conferidos com {check.Points} pontos dos caminhos de pista):";
        yield return $"  melhor: {check.Best.Name}, erro médio {check.Best.MeanErrorMeters.ToString("0.0", inv)} m";
        yield return $"  segundo: {check.Ranking[1].Name}, erro médio {check.Ranking[1].MeanErrorMeters.ToString("0.0", inv)} m";
        yield return check.Convincing ? "  conclusão: a convenção é a melhor, com folga." : "  conclusão: NÃO é conclusivo (o melhor não se destaca ou erra muito); me traga o arquivo para eu olhar.";
    }

    private static string Mhz(int kHz) => $"{kHz / 1000}.{kHz % 1000:D3}";
}
