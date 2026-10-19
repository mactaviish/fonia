using System.Diagnostics;

namespace Fonia.Sim.Msfs;

/// <summary>
/// Asks MSFS what it knows about an airport (runways, radio frequencies, taxi graph, parking): the facility data of SimConnect. Read only. It is
/// a spike's tool: it returns the data as the simulator gave it, with how each request went, so that what the simulator really says can be
/// seen before anything is built on it. Each part of the airport is its own request, so a part the simulator refuses does not cost the others.
/// </summary>
public sealed class MsfsFacilityReader
{
    /// <summary>How long a request may take to end. A big airport has thousands of items, but they come in well under this.</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);

    // The ids only have to differ from one another inside this connection.
    private const uint FirstDefinitionId = 100;
    private const uint FirstRequestId = 1000;

    private readonly Func<IFacilityTransport> _transportFactory;
    private readonly TimeSpan _requestTimeout;

    public MsfsFacilityReader(TimeSpan? requestTimeout = null)
        : this(() => new SimConnectNetFacilityTransport(), requestTimeout ?? DefaultRequestTimeout)
    {
    }

    internal MsfsFacilityReader(Func<IFacilityTransport> transportFactory, TimeSpan requestTimeout)
    {
        _transportFactory = transportFactory;
        _requestTimeout = requestTimeout;
    }

    /// <summary>True when the last read found the simulator to be MSFS 2024, false for 2020, null when the library could not tell.</summary>
    public bool? IsMsfs2024 { get; private set; }

    /// <exception cref="FacilityReadException">MSFS cannot be reached.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public async Task<AirportFacility> ReadAsync(string icao, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);

        var code = icao.Trim().ToUpperInvariant();
        var clock = Stopwatch.StartNew();

        await using var transport = _transportFactory();
        var collector = new Collector();

        transport.MessageReceived += collector.OnMessage;

        await transport.OpenAsync(ct);

        IsMsfs2024 = transport.IsMsfs2024;

        var outcomes = new List<FacilityRequestOutcome>();
        var results = new Dictionary<string, List<FacilityDataMessage>>();
        var defineId = FirstDefinitionId;
        var requestId = FirstRequestId;

        foreach (var schema in FacilitySchemas())
        {
            ct.ThrowIfCancellationRequested();

            var started = clock.Elapsed;
            var problems = new List<string>();
            var items = new List<FacilityDataMessage>();

            collector.Begin(requestId);

            var defined = transport.Define(defineId, schema.Definition());
            var asked = defined == 0 ? transport.Request(defineId, requestId, code) : defined;

            if (asked != 0)
            {
                problems.Add($"{(defined != 0 ? "definition" : "request")} refused by SimConnect (code 0x{asked:X8})");
                outcomes.Add(new FacilityRequestOutcome(schema.Name, 0, false, clock.Elapsed - started, problems));
                defineId++;
                requestId++;
                continue;
            }

            var ended = await collector.WaitAsync(_requestTimeout, ct);
            var (received, notes) = collector.Take(schema.Kind);

            problems.AddRange(notes);
            items.AddRange(received);

            if (!ended)
            {
                problems.Add($"no end of data in {_requestTimeout.TotalSeconds:0} s");
            }

            results[schema.Name] = items;
            outcomes.Add(new FacilityRequestOutcome(schema.Name, items.Count, ended, clock.Elapsed - started, problems));
            defineId++;
            requestId++;
        }

        return Assemble(results, outcomes, clock.Elapsed);
    }

    private static IReadOnlyList<FacilitySchema> FacilitySchemas() => FacilitySchema.All;

    private static AirportFacility Assemble(Dictionary<string, List<FacilityDataMessage>> results, List<FacilityRequestOutcome> outcomes, TimeSpan elapsed)
    {
        var problems = new Dictionary<string, List<string>>();

        List<T> Parse<T>(FacilitySchema schema, Func<FacilityDataMessage, T> parse)
        {
            var list = new List<T>();

            foreach (var message in results.GetValueOrDefault(schema.Name) ?? [])
            {
                if (message.Data.Length < schema.Size)
                {
                    Note(problems, schema.Name, $"item {message.ItemIndex} has {message.Data.Length} bytes, {schema.Size} were expected");
                    continue;
                }

                try
                {
                    list.Add(parse(message));
                }
                catch (FormatException ex)
                {
                    Note(problems, schema.Name, $"item {message.ItemIndex}: {ex.Message}");
                }
            }

            return list;
        }

        var airport = Parse(FacilitySchema.Airport, m => FacilityParser.Airport(m.Data)).FirstOrDefault();
        var runways = Parse(FacilitySchema.Runway, m => FacilityParser.Runway(m.Data));
        var frequencies = Parse(FacilitySchema.Frequency, m => FacilityParser.Frequency(m.Data));
        var points = Parse(FacilitySchema.TaxiPoint, m => FacilityParser.TaxiPoint((int)m.ItemIndex, m.Data));
        var paths = Parse(FacilitySchema.TaxiPath, m => FacilityParser.TaxiPath(m.Data));
        var names = Parse(FacilitySchema.TaxiName, m => FacilityParser.TaxiName((int)m.ItemIndex, m.Data));
        var parkings = Parse(FacilitySchema.TaxiParking, m => FacilityParser.Parking((int)m.ItemIndex, m.Data));

        var withParseProblems = outcomes
            .Select(o => problems.TryGetValue(o.Name, out var extra) ? o with { Problems = [.. o.Problems, .. extra] } : o)
            .ToList();

        return new AirportFacility(airport, runways, frequencies, points, paths, names, parkings, withParseProblems, elapsed);
    }

    private static void Note(Dictionary<string, List<string>> problems, string name, string text)
    {
        if (!problems.TryGetValue(name, out var list))
        {
            problems[name] = list = [];
        }

        list.Add(text);
    }

    // What arrives from the library's threads while one request is waited for. One request at a time: what comes is of the one in flight.
    private sealed class Collector
    {
        private readonly Lock _gate = new();
        private readonly List<FacilityDataMessage> _items = [];
        private readonly List<string> _notes = [];
        private TaskCompletionSource<bool> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private uint _current;

        public void Begin(uint requestId)
        {
            lock (_gate)
            {
                _current = requestId;
                _items.Clear();
                _notes.Clear();
                _ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void OnMessage(FacilityMessage message)
        {
            lock (_gate)
            {
                switch (message)
                {
                    case FacilityDataMessage data when data.RequestId == _current:
                        _items.Add(data);
                        break;
                    case FacilityEndMessage end when end.RequestId == _current:
                        _ended.TrySetResult(true);
                        break;
                    case FacilityExceptionMessage refused:
                        _notes.Add($"SimConnect exception {refused.Code} (parameter {refused.Index}, send id {refused.SendId})");
                        break;
                    case FacilityAmbiguousMessage:
                        _notes.Add("the code matches more than one facility; the simulator sent a list to choose from");
                        _ended.TrySetResult(true);
                        break;
                }
            }
        }

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)
        {
            Task<bool> waiting;

            lock (_gate)
            {
                waiting = _ended.Task;
            }

            try
            {
                return await waiting.WaitAsync(timeout, ct);
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        public (List<FacilityDataMessage> Items, List<string> Notes) Take(FacilityKind kind)
        {
            lock (_gate)
            {
                return ([.. _items.Where(m => m.Kind == kind)], [.. _notes]);
            }
        }
    }
}
