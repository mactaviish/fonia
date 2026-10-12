// SimConnect spike (M1, task 2). Throwaway: answers the open questions in docs/decisions/simconnect.md.
//
//   dotnet run --project spikes/SimConnectSpike -c Release -- [--seconds 30] [--wait-seconds 120]
//
// --seconds       how long to read after connecting (0 = until Ctrl+C). Default 30.
// --wait-seconds  how long to keep retrying while MSFS is not up yet. Default 120.
//
// Everything printed is also written to spike-<timestamp>.log in the current directory.

using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using SimConnect.NET;
using SimConnect.NET.SimVar;

var seconds = ReadIntOption("--seconds", 30);
var waitSeconds = ReadIntOption("--wait-seconds", 120);

var logPath = $"spike-{DateTime.Now:yyyyMMdd-HHmmss}.log";
using var logFile = new StreamWriter(logPath, append: false) { AutoFlush = true };
var logLock = new object();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Log($"Fonia SimConnect spike  |  log file: {Path.GetFullPath(logPath)}");
Log($"OS: {Environment.OSVersion}  |  runtime: {Environment.Version}  |  64-bit process: {Environment.Is64BitProcess}");
Log($"Spike process elevated (admin): {IsElevated()}");
Log($"MSFS 2020 process running ('FlightSimulator'): {Process.GetProcessesByName("FlightSimulator").Length > 0}");
Log($"MSFS 2024 process running ('FlightSimulator2024'): {Process.GetProcessesByName("FlightSimulator2024").Length > 0}");
Log(string.Empty);

var stats = new Stats();
RadiosHz? latestRadiosHz = null;
RadiosMhz? latestRadiosMhz = null;
var subscriptions = new List<ISimVarSubscription>();
SimConnectClient? client = null;

// ---- 1. Connect (retrying, to also test "app started before the simulator") ----
var connectDeadline = DateTime.UtcNow.AddSeconds(waitSeconds);
var attempt = 0;

while (client is null && !cts.IsCancellationRequested)
{
    attempt++;
    var candidate = new SimConnectClient("Fonia Spike");
    try
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        attemptCts.CancelAfter(TimeSpan.FromSeconds(10));

        Log($"Connect attempt {attempt}...");
        await candidate.ConnectAsync(cancellationToken: attemptCts.Token);

        client = candidate;
        Log($"CONNECTED. IsConnected={candidate.IsConnected}  IsMSFS2024={candidate.IsMSFS2024}");
    }
    catch (Exception ex)
    {
        Log($"  attempt {attempt} failed: {ex.GetType().Name}: {ex.Message}");
        await candidate.DisposeAsync();

        if (DateTime.UtcNow >= connectDeadline)
        {
            Log("Gave up waiting for the simulator.");
            return 1;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }
}

if (client is null)
{
    Log("Cancelled before connecting.");
    return 1;
}

client.ConnectionStatusChanged += (_, e) => Log($"EVENT connection changed: {e.PreviousStatus} -> {e.CurrentStatus}");
client.ErrorOccurred += (_, e) => Log($"EVENT error: {e.Error} | context: {e.Context} | recoverable: {e.IsRecoverable} | {e.Exception?.Message}");

// Does the library bring the connection back by itself if MSFS is closed and reopened?
client.SystemEventReceived += (_, e) => Log($"EVENT system id={e.EventId} data={e.Data}");
client.FilenameEventReceived += (_, e) => Log($"EVENT filename '{e.FileName}' flags={e.Flags}");
foreach (var (eventName, eventId) in new (string, uint)[] { ("Sim", 1), ("SimStart", 2), ("SimStop", 3), ("Pause", 4), ("FlightLoaded", 5) })
{
    try
    {
        await client.SubscribeToEventAsync(eventName, eventId, cts.Token);
        Log($"subscribed to system event '{eventName}' as id {eventId}");
    }
    catch (Exception ex)
    {
        Log($"system event '{eventName}' FAILED: {ex.GetType().Name}: {ex.Message}");
    }
}

client.AutoReconnectEnabled = true;
client.ReconnectDelay = TimeSpan.FromSeconds(2);
Log($"AutoReconnectEnabled={client.AutoReconnectEnabled} ReconnectDelay={client.ReconnectDelay} MaxReconnectAttempts={client.MaxReconnectAttempts}");
Log(string.Empty);

// ---- 2. Subscribe, one group at a time so a bad unit only breaks its own group ----
Start<Motion>("Motion", m => string.Create(
    CultureInfo.InvariantCulture,
    $"lat={m.Latitude:F5} lon={m.Longitude:F5} msl={m.AltitudeMsl:F0}ft agl={m.AltitudeAgl:F0}ft gs={m.GroundSpeed:F1}kt hdgM={m.HeadingMagnetic:F0} onGround={m.OnGround}"));
Start<Identity>("Identity", i => $"ATC ID='{i.AtcId}' ATC TYPE='{i.AtcType}' ATC MODEL='{i.AtcModel}' TITLE='{i.Title}'");
Start<RadiosMhz>("RadiosMhz", r =>
{
    latestRadiosMhz = r;
    return string.Create(
        CultureInfo.InvariantCulture,
        $"COM1 {r.Com1Active:F3}/{r.Com1Standby:F3}  COM2 {r.Com2Active:F3}/{r.Com2Standby:F3} (MHz; active/standby)");
});
Start<RadiosHz>("RadiosHz", r =>
{
    latestRadiosHz = r;
    return string.Create(
        CultureInfo.InvariantCulture,
        $"COM1 {r.Com1Active:F0}/{r.Com1Standby:F0}  COM2 {r.Com2Active:F0}/{r.Com2Standby:F0} (Hz; active/standby)");
});
Start<SimState>("SimState", x => string.Create(CultureInfo.InvariantCulture, $"simDisabled={x.SimDisabled} simulationRate={x.SimulationRate:F2}"));
Start<TransponderNumber>("TransponderNumber", t => $"unit 'number' -> code={t.Code} state={t.State}");
Start<TransponderBcd>("TransponderBcd", t => $"unit 'bco16' -> {t.Code} (0x{t.Code:X4})");

// ---- 3. Read, report every 2 s ----
await Crosscheck("start");

var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
var wallStart = Stopwatch.StartNew();
Log(seconds == 0
    ? "Reading until Ctrl+C. Change COM frequencies / squawk in the cockpit; close and reopen MSFS to test reconnection."
    : $"Reading for {seconds} s (Ctrl+C stops earlier). Change COM frequencies / squawk in the cockpit now.");
Log(string.Empty);

while (!cts.IsCancellationRequested && (seconds == 0 || wallStart.Elapsed.TotalSeconds < seconds))
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }

    Log($"--- t={wallStart.Elapsed.TotalSeconds:F0}s  connected={client.IsConnected}");
    stats.Report(Log);
}

// ---- 4. Summary ----
await Crosscheck("end");
await CompareRadios();

var cpuUsed = Process.GetCurrentProcess().TotalProcessorTime - cpuStart;
var cpuPercentOfOneCore = cpuUsed.TotalSeconds / wallStart.Elapsed.TotalSeconds * 100;
Log(string.Empty);
Log("=== SUMMARY ===");
stats.Summary(Log, wallStart.Elapsed.TotalSeconds);
Log($"Spike CPU: {cpuPercentOfOneCore:F2}% of one core on average ({Environment.ProcessorCount} logical cores); working set {Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024} MB");
Log($"Still connected at the end: {client.IsConnected}");

foreach (var subscription in subscriptions)
{
    subscription.Dispose();
}

await client.DisposeAsync();
Log($"Done. Send me the file: {Path.GetFullPath(logPath)}");
return 0;

// ---------------------------------------------------------------------------

void Start<T>(string name, Func<T, string> format)
    where T : struct
{
    try
    {
        var subscription = client!.SimVars.Subscribe<T>(SimConnectPeriod.Second, value => stats.Record(name, format(value)));
        subscriptions.Add(subscription);
        stats.Register(name);

        _ = subscription.Completion.ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    Log($"[{name}] subscription FAULTED: {task.Exception?.GetBaseException().Message}");
                    stats.MarkFailed(name, task.Exception?.GetBaseException().Message ?? "faulted");
                }
            },
            TaskScheduler.Default);

        Log($"[{name}] subscribed");
    }
    catch (Exception ex)
    {
        Log($"[{name}] FAILED to subscribe: {ex.GetType().Name}: {ex.Message}");
        stats.Register(name);
        stats.MarkFailed(name, ex.Message);
    }
}

// Reads the same variables one by one through the library's scalar path, to tell a library or
// struct-layout problem apart from what the simulator/aircraft really reports.
async Task Crosscheck(string label)
{
    (string Name, string Unit)[] variables =
    [
        ("COM ACTIVE FREQUENCY:1", "MHz"),
        ("COM STANDBY FREQUENCY:1", "MHz"),
        ("COM ACTIVE FREQUENCY:2", "MHz"),
        ("COM STANDBY FREQUENCY:2", "MHz"),
        ("COM ACTIVE FREQUENCY:1", "Hz"),
        ("COM ACTIVE FREQUENCY:2", "Hz"),
        ("TRANSPONDER CODE:1", "number"),
        ("TRANSPONDER CODE:1", "bco16"),
        ("TRANSPONDER STATE:1", "number"),
    ];

    Log($"CROSSCHECK ({label}): one-by-one scalar reads");

    foreach (var (name, unit) in variables)
    {
        try
        {
            var value = await client!.SimVars.GetAsync<double>(name, unit, cancellationToken: cts.Token);
            Log(string.Create(CultureInfo.InvariantCulture, $"  {name} [{unit}] = {value:F3}"));
        }
        catch (Exception ex)
        {
            Log($"  {name} [{unit}] FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    Log(string.Empty);
}

// The structs hold the latest value of each subscription; compare them with a fresh scalar read.
// Radios not changed during the run should MATCH. Values can legitimately differ by up to ~1 s of age.
async Task CompareRadios()
{
    Log("COMPARE struct (latest subscription value) vs scalar (fresh read)");

    (string Name, string Unit, double? Struct)[] checks =
    [
        ("COM ACTIVE FREQUENCY:1", "Hz", latestRadiosHz?.Com1Active),
        ("COM STANDBY FREQUENCY:1", "Hz", latestRadiosHz?.Com1Standby),
        ("COM ACTIVE FREQUENCY:2", "Hz", latestRadiosHz?.Com2Active),
        ("COM STANDBY FREQUENCY:2", "Hz", latestRadiosHz?.Com2Standby),
        ("COM ACTIVE FREQUENCY:1", "MHz", latestRadiosMhz?.Com1Active),
        ("COM STANDBY FREQUENCY:1", "MHz", latestRadiosMhz?.Com1Standby),
        ("COM ACTIVE FREQUENCY:2", "MHz", latestRadiosMhz?.Com2Active),
        ("COM STANDBY FREQUENCY:2", "MHz", latestRadiosMhz?.Com2Standby),
    ];

    foreach (var (name, unit, structValue) in checks)
    {
        try
        {
            var scalar = await client!.SimVars.GetAsync<double>(name, unit, cancellationToken: cts.Token);
            var verdict = structValue is { } v && Math.Abs(v - scalar) < 0.0005 * Math.Max(1, Math.Abs(scalar)) ? "MATCH" : "MISMATCH";
            Log(string.Create(CultureInfo.InvariantCulture, $"  {verdict,-8} {name} [{unit}]  struct={structValue?.ToString("F3", CultureInfo.InvariantCulture) ?? "n/a"}  scalar={scalar:F3}"));
        }
        catch (Exception ex)
        {
            Log($"  {name} [{unit}] FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    Log(string.Empty);
}

void Log(string message)
{
    var line = message.Length == 0 ? string.Empty : $"{DateTime.Now:HH:mm:ss.fff}  {message}";

    lock (logLock)
    {
        Console.WriteLine(line);
        logFile.WriteLine(line);
    }
}

int ReadIntOption(string name, int fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : fallback;
}

static bool IsElevated()
{
    return OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
}

// ---------------------------------------------------------------------------
// One struct per group of variables. Units are the ones we plan to use, plus alternatives
// (Hz, bco16) to find out which one MSFS 2020 actually accepts.
// ---------------------------------------------------------------------------

public struct Motion
{
    [SimConnect("PLANE LATITUDE", "degrees")]
    public double Latitude;

    [SimConnect("PLANE LONGITUDE", "degrees")]
    public double Longitude;

    [SimConnect("PLANE ALTITUDE", "feet")]
    public double AltitudeMsl;

    [SimConnect("PLANE ALT ABOVE GROUND", "feet")]
    public double AltitudeAgl;

    [SimConnect("GROUND VELOCITY", "knots")]
    public double GroundSpeed;

    [SimConnect("PLANE HEADING DEGREES MAGNETIC", "degrees")]
    public double HeadingMagnetic;

    [SimConnect("SIM ON GROUND", "bool", SimConnectDataType.Integer32)]
    public int OnGround;
}

public struct Identity
{
    [SimConnect("ATC ID", SimConnectDataType.String32)]
    public string AtcId;

    [SimConnect("ATC TYPE", SimConnectDataType.String32)]
    public string AtcType;

    [SimConnect("ATC MODEL", SimConnectDataType.String32)]
    public string AtcModel;

    [SimConnect("TITLE", SimConnectDataType.String128)]
    public string Title;
}

public struct RadiosMhz
{
    [SimConnect("COM ACTIVE FREQUENCY:1", "MHz")]
    public double Com1Active;

    [SimConnect("COM STANDBY FREQUENCY:1", "MHz")]
    public double Com1Standby;

    [SimConnect("COM ACTIVE FREQUENCY:2", "MHz")]
    public double Com2Active;

    [SimConnect("COM STANDBY FREQUENCY:2", "MHz")]
    public double Com2Standby;
}

public struct RadiosHz
{
    [SimConnect("COM ACTIVE FREQUENCY:1", "Hz")]
    public double Com1Active;

    [SimConnect("COM STANDBY FREQUENCY:1", "Hz")]
    public double Com1Standby;

    [SimConnect("COM ACTIVE FREQUENCY:2", "Hz")]
    public double Com2Active;

    [SimConnect("COM STANDBY FREQUENCY:2", "Hz")]
    public double Com2Standby;
}

public struct SimState
{
    [SimConnect("SIM DISABLED", "bool", SimConnectDataType.Integer32)]
    public int SimDisabled;

    [SimConnect("SIMULATION RATE", "number")]
    public double SimulationRate;
}

public struct TransponderNumber
{
    [SimConnect("TRANSPONDER CODE:1", "number", SimConnectDataType.Integer32)]
    public int Code;

    [SimConnect("TRANSPONDER STATE:1", "number", SimConnectDataType.Integer32)]
    public int State;
}

public struct TransponderBcd
{
    [SimConnect("TRANSPONDER CODE:1", "bco16", SimConnectDataType.Integer32)]
    public int Code;
}

/// <summary>Thread-safe counters: callbacks arrive on SimConnect.NET's own threads.</summary>
public sealed class Stats
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Group> _groups = [];

    public void Register(string name)
    {
        lock (_gate)
        {
            _groups.TryAdd(name, new Group());
        }
    }

    public void Record(string name, string formatted)
    {
        lock (_gate)
        {
            var group = _groups[name];
            group.Total++;
            group.SinceLastReport++;
            group.Latest = formatted;
            group.FirstSeen ??= formatted;
        }
    }

    public void MarkFailed(string name, string reason)
    {
        lock (_gate)
        {
            _groups[name].Failure = reason;
        }
    }

    public void Report(Action<string> log)
    {
        lock (_gate)
        {
            foreach (var (name, group) in _groups)
            {
                if (group.Failure is not null)
                {
                    log($"[{name}] FAILED: {group.Failure}");
                    continue;
                }

                log($"[{name}] +{group.SinceLastReport} (total {group.Total})  {group.Latest ?? "(no data yet)"}");
                group.SinceLastReport = 0;
            }
        }
    }

    public void Summary(Action<string> log, double seconds)
    {
        lock (_gate)
        {
            foreach (var (name, group) in _groups)
            {
                var rate = seconds > 0 ? group.Total / seconds : 0;
                log(group.Failure is not null
                    ? $"[{name}] FAILED: {group.Failure}"
                    : $"[{name}] {group.Total} updates (~{rate:F2}/s). first: {group.FirstSeen ?? "-"} | last: {group.Latest ?? "-"}");
            }
        }
    }

    private sealed class Group
    {
        public int Total { get; set; }

        public int SinceLastReport { get; set; }

        public string? Latest { get; set; }

        public string? FirstSeen { get; set; }

        public string? Failure { get; set; }
    }
}
