// SimConnect spike (M1, task 2). Throwaway: answers the open questions in docs/decisions/simconnect.md.
//
//   dotnet run --project spikes/SimConnectSpike -c Release -- [--seconds 30] [--wait-seconds 0]
//
// --seconds       total run time (0 = until Ctrl+C). Default 30. Use 0 to test reconnection.
// --wait-seconds  give up connecting after this many seconds. Default 0 = never give up (retry
//                 until Ctrl+C), which is what the real adapter will do: MSFS can take minutes to start.
//
// Reconnection experiment: SimConnect.NET 0.2.2 never noticed MSFS quitting (IsConnected stayed
// true, no events, subscriptions silently stopped). This spike therefore detects a dead connection
// itself, from several signals, logs which one fires first, then disposes the client, connects
// again with a NEW client and re-subscribes. Close and reopen MSFS while it runs.
//
// Everything printed is also written to spike-<timestamp>.log in the current directory.

using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using SimConnect.NET;
using SimConnect.NET.SimVar;

var seconds = ReadIntOption("--seconds", 30);
var waitSeconds = ReadIntOption("--wait-seconds", 0);

var logPath = $"spike-{DateTime.Now:yyyyMMdd-HHmmss}.log";
using var logFile = new StreamWriter(logPath, append: false) { AutoFlush = true };
var logLock = new object();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Log($"Fonia SimConnect spike v2  |  log file: {Path.GetFullPath(logPath)}");
Log($"OS: {Environment.OSVersion}  |  runtime: {Environment.Version}  |  64-bit process: {Environment.Is64BitProcess}");
Log($"Spike process elevated (admin): {IsElevated()}");
Log($"MSFS 2020 process running ('FlightSimulator'): {Process.GetProcessesByName("FlightSimulator").Length > 0}");
Log($"MSFS 2024 process running ('FlightSimulator2024'): {Process.GetProcessesByName("FlightSimulator2024").Length > 0}");
Log(string.Empty);

var total = Stopwatch.StartNew();
var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
var sessionNumber = 0;
DateTime? deadSince = null;

// Per-session state, replaced on every (re)connection.
SimConnectClient? client = null;
Stats stats = new();
var subscriptions = new List<ISimVarSubscription>();
RadiosHz? latestRadiosHz = null;
RadiosMhz? latestRadiosMhz = null;
var lastMotionUtc = DateTime.UtcNow;
var simRunning = false;
var sessionEnding = false;
TaskCompletionSource<string> dead = new();

while (!cts.IsCancellationRequested && !TimeIsUp())
{
    sessionNumber++;
    var connected = await ConnectAsync();
    if (!connected)
    {
        break;
    }

    if (deadSince is { } since)
    {
        Log($"RECOVERED after {(DateTime.UtcNow - since).TotalSeconds:F1} s (session {sessionNumber}).");
        deadSince = null;
    }

    await RunSessionAsync();

    var reason = dead.Task.IsCompleted ? dead.Task.Result : null;
    await EndSessionAsync(clean: reason is null);

    if (reason is not null)
    {
        deadSince = DateTime.UtcNow;
        Log($"SESSION {sessionNumber} DEAD ({reason}). Connecting again with a new client...");
        Log(string.Empty);
    }
}

var cpuUsed = Process.GetCurrentProcess().TotalProcessorTime - cpuStart;
Log(string.Empty);
Log($"Spike CPU over the whole run: {cpuUsed.TotalSeconds / total.Elapsed.TotalSeconds * 100:F2}% of one core ({Environment.ProcessorCount} logical cores); working set {Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024} MB");
Log($"Sessions: {sessionNumber}. Done. Send me the file: {Path.GetFullPath(logPath)}");
return 0;

// ---------------------------------------------------------------------------

bool TimeIsUp() => seconds != 0 && total.Elapsed.TotalSeconds >= seconds;

async Task<bool> ConnectAsync()
{
    var deadline = waitSeconds == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(waitSeconds);
    var attempt = 0;

    while (!cts.IsCancellationRequested)
    {
        attempt++;
        var candidate = new SimConnectClient("Fonia Spike");

        try
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            attemptCts.CancelAfter(TimeSpan.FromSeconds(10));

            if (attempt <= 3 || attempt % 10 == 0)
            {
                Log($"[session {sessionNumber}] connect attempt {attempt}...");
            }

            await candidate.ConnectAsync(cancellationToken: attemptCts.Token);

            client = candidate;
            stats = new Stats();
            subscriptions = [];
            latestRadiosHz = null;
            latestRadiosMhz = null;
            lastMotionUtc = DateTime.UtcNow;
            simRunning = false;
            sessionEnding = false;
            dead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            Log($"CONNECTED after {attempt} attempt(s). IsConnected={candidate.IsConnected}  IsMSFS2024={candidate.IsMSFS2024}");
            return true;
        }
        catch (Exception ex)
        {
            if (attempt <= 3 || attempt % 10 == 0)
            {
                Log($"  attempt {attempt} failed: {ex.GetType().Name}: {ex.Message}");
            }

            await DisposeQuietlyAsync(candidate);

            if (DateTime.UtcNow >= deadline)
            {
                Log("Gave up waiting for the simulator.");
                return false;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    return false;
}

async Task RunSessionAsync()
{
    var c = client!;
    var seen = new HashSet<SimConnectRecvId>();

    c.AutoReconnectEnabled = false; // we own reconnection in this experiment
    c.ConnectionStatusChanged += (_, e) =>
    {
        Log($"EVENT connection changed: {e.PreviousStatus} -> {e.CurrentStatus}");
        if (!e.CurrentStatus)
        {
            MarkDead("ConnectionStatusChanged -> disconnected");
        }
    };
    c.ErrorOccurred += (_, e) => Log($"EVENT error: {e.Error} | context: {e.Context} | recoverable: {e.IsRecoverable} | {e.Exception?.Message}");
    c.RawMessageReceived += (_, e) =>
    {
        bool first;
        lock (seen)
        {
            first = seen.Add(e.MessageId);
        }

        if (first)
        {
            Log($"RAW message type seen for the first time: {e.MessageId}");
        }

        if (e.MessageId == SimConnectRecvId.Quit)
        {
            MarkDead("raw SIMCONNECT_RECV_QUIT message");
        }
    };
    c.SystemEventReceived += (_, e) =>
    {
        Log($"EVENT system id={e.EventId} data={e.Data}");
        if (e.EventId == 1)
        {
            simRunning = e.Data == 1;
        }
    };
    c.FilenameEventReceived += (_, e) => Log($"EVENT filename '{e.FileName}' flags={e.Flags}");

    foreach (var (eventName, eventId) in new (string, uint)[] { ("Sim", 1), ("SimStart", 2), ("SimStop", 3), ("Pause", 4), ("FlightLoaded", 5) })
    {
        try
        {
            await c.SubscribeToEventAsync(eventName, eventId, cts.Token);
        }
        catch (Exception ex)
        {
            Log($"system event '{eventName}' FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    Start<Motion>("Motion", m =>
    {
        lastMotionUtc = DateTime.UtcNow;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"lat={m.Latitude:F5} lon={m.Longitude:F5} msl={m.AltitudeMsl:F0}ft agl={m.AltitudeAgl:F0}ft gs={m.GroundSpeed:F1}kt hdgM={m.HeadingMagnetic:F0} onGround={m.OnGround}");
    });
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

    using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
    var watchers = new[]
    {
        WatchProbeAsync(c, sessionCts.Token),
        WatchProcessAsync(sessionCts.Token),
        WatchStaleDataAsync(sessionCts.Token),
    };

    Log(seconds == 0
        ? "Reading until Ctrl+C. Close and reopen MSFS (and load a flight) to test recovery."
        : $"Reading until {seconds} s of total run time (Ctrl+C stops earlier).");
    Log(string.Empty);

    var sessionClock = Stopwatch.StartNew();
    var lastReport = TimeSpan.Zero;

    while (!cts.IsCancellationRequested && !TimeIsUp() && !dead.Task.IsCompleted)
    {
        try
        {
            await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(1), cts.Token), dead.Task);
        }
        catch (OperationCanceledException)
        {
            break;
        }

        // Every 2 s during the first 10 s of a session, then every 10 s: keeps the log readable.
        var interval = sessionClock.Elapsed < TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(10);
        if (sessionClock.Elapsed - lastReport >= interval)
        {
            lastReport = sessionClock.Elapsed;
            Log($"--- session {sessionNumber} t={sessionClock.Elapsed.TotalSeconds:F0}s  IsConnected={c.IsConnected}");
            stats.Report(Log);
        }
    }

    await sessionCts.CancelAsync();
    await Task.WhenAll(watchers.Select(w => w.ContinueWith(_ => { }, TaskScheduler.Default)));
}

async Task EndSessionAsync(bool clean)
{
    sessionEnding = true; // disposing raises a disconnect event; that is not a failure
    Log(string.Empty);
    Log($"=== SESSION {sessionNumber} SUMMARY ({(clean ? "clean end" : "connection died")}) ===");
    stats.Summary(Log, Math.Max(1, total.Elapsed.TotalSeconds));

    if (clean && client is not null)
    {
        await Crosscheck("end");
        await CompareRadios();
    }

    foreach (var subscription in subscriptions)
    {
        try
        {
            subscription.Dispose();
        }
        catch (Exception ex)
        {
            Log($"  (dispose subscription failed: {ex.GetType().Name}: {ex.Message})");
        }
    }

    if (client is not null)
    {
        await DisposeQuietlyAsync(client);
        client = null;
    }
}

// Dispose can hang or throw on a dead connection: never let it block recovery.
async Task DisposeQuietlyAsync(SimConnectClient target)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var dispose = target.DisposeAsync().AsTask();
        var finished = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(5)));
        Log(finished == dispose
            ? $"  client disposed in {sw.ElapsedMilliseconds} ms"
            : "  client dispose did NOT finish within 5 s (abandoned)");
    }
    catch (Exception ex)
    {
        Log($"  client dispose threw: {ex.GetType().Name}: {ex.Message}");
    }
}

void MarkDead(string reason)
{
    if (!sessionEnding && dead.TrySetResult(reason))
    {
        Log($"!!! CONNECTION CONSIDERED DEAD: {reason}");
    }
}

// Signal 1: ask the library whether the connection still works, every 3 s.
async Task WatchProbeAsync(SimConnectClient c, CancellationToken token)
{
    var failures = 0;
    while (!token.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            probeCts.CancelAfter(TimeSpan.FromSeconds(3));
            var sw = Stopwatch.StartNew();

            bool ok;
            try
            {
                ok = await c.TestConnectionAsync(probeCts.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                Log($"PROBE timed out after {sw.ElapsedMilliseconds} ms");
                ok = false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log($"PROBE threw {ex.GetType().Name}: {ex.Message}");
                ok = false;
            }

            if (ok)
            {
                failures = 0;
                continue;
            }

            failures++;
            Log($"PROBE failed ({failures} in a row)");
            if (failures >= 2)
            {
                MarkDead("TestConnectionAsync failed twice in a row");
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }
}

// Signal 2 (information only): is the MSFS process still there? Tells us the detection latency.
async Task WatchProcessAsync(CancellationToken token)
{
    int? lastPid = CurrentMsfsPid();
    DateTime? goneAt = null;

    while (!token.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var pid = CurrentMsfsPid();
        if (pid != lastPid)
        {
            Log(pid is null ? "PROCESS: MSFS process disappeared" : $"PROCESS: MSFS process now pid {pid} (was {lastPid?.ToString() ?? "none"})");
            goneAt = pid is null ? DateTime.UtcNow : null;
            lastPid = pid;
        }

        if (goneAt is { } gone && dead.Task.IsCompleted)
        {
            Log($"PROCESS: detection latency = {(DateTime.UtcNow - gone).TotalSeconds:F1} s after the process disappeared");
            goneAt = null;
        }
    }
}

// Signal 3 (information only): flight running (Sim=1) but no position data for a while.
async Task WatchStaleDataAsync(CancellationToken token)
{
    var warned = false;
    while (!token.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var age = (DateTime.UtcNow - lastMotionUtc).TotalSeconds;
        if (simRunning && age > 6)
        {
            if (!warned)
            {
                Log($"STALE: Sim=1 but no Motion data for {age:F0} s");
                warned = true;
            }
        }
        else
        {
            warned = false;
        }
    }
}

static int? CurrentMsfsPid()
{
    var processes = Process.GetProcessesByName("FlightSimulator");
    return processes.Length > 0 ? processes[0].Id : null;
}

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
    }
    catch (Exception ex)
    {
        Log($"[{name}] FAILED to subscribe: {ex.GetType().Name}: {ex.Message}");
        stats.Register(name);
        stats.MarkFailed(name, ex.Message);
    }
}

// Reads variables one by one through the library's scalar path, to tell a library or
// struct-layout problem apart from what the simulator/aircraft really reports.
async Task Crosscheck(string label)
{
    (string Name, string Unit)[] variables =
    [
        ("COM ACTIVE FREQUENCY:1", "Hz"),
        ("COM STANDBY FREQUENCY:1", "Hz"),
        ("COM ACTIVE FREQUENCY:2", "Hz"),
        ("COM STANDBY FREQUENCY:2", "Hz"),
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
        ("COM ACTIVE FREQUENCY:2", "MHz", latestRadiosMhz?.Com2Active),
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
