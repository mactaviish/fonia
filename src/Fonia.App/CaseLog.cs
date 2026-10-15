using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Fonia.Core.Atc;
using Fonia.Core.Audio;
using Fonia.Core.Phraseology;
using Fonia.Core.Pipeline;

namespace Fonia.App;

/// <summary>
/// The cases of a session in which the controller could not follow the pilot (or the pilot said it got it wrong), kept on the user's
/// disk to improve the app: <c>%LOCALAPPDATA%\Fonia\cases\yyyyMMdd-HHmmss\cases.jsonl</c>, one JSON object per line, and, only when the
/// user allowed it, the sound of the pilot beside it (<c>001.wav</c>). Nothing here leaves the computer. A disk that fails is never a
/// reason to stop the radio: writing is best effort.
/// </summary>
/// <remarks>
/// The lines are <c>session</c> (the first one), <c>case</c> (an exchange worth keeping, with its reasons) and <c>mark</c> (the pilot
/// said that exchange was wrong). An <c>id</c> counts every exchange of the session, so the ones that went well leave gaps.
/// </remarks>
internal sealed class CaseLog
{
    private const string SessionPattern = "yyyyMMdd-HHmmss";
    private const string FileName = "cases.jsonl";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly IPhraseology _phraseology;
    private readonly AirportContext _airport;
    private readonly string _callsign;
    private readonly bool _saveAudio;
    private readonly DateTimeOffset _startedAt;
    private int _lastId;
    private int _cases;
    private ExchangeResult? _last;
    private bool _lastKept;
    private bool _lastMarked;
    private bool _headerWritten;

    public CaseLog(string root, TimeProvider time, IPhraseology phraseology, AirportContext airport, string callsign, bool saveAudio)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(phraseology);
        ArgumentNullException.ThrowIfNull(airport);

        _time = time;
        _phraseology = phraseology;
        _airport = airport;
        _callsign = callsign;
        _saveAudio = saveAudio;
        _startedAt = time.GetUtcNow();
        SessionDirectory = Path.Combine(root, _startedAt.UtcDateTime.ToString(SessionPattern, CultureInfo.InvariantCulture));
    }

    /// <summary>Where this session's files go. It is created when the first case is written.</summary>
    public string SessionDirectory { get; }

    /// <summary>The cases kept in this session.</summary>
    public int Cases
    {
        get
        {
            lock (_gate)
            {
                return _cases;
            }
        }
    }

    /// <summary>The default place: <c>%LOCALAPPDATA%\Fonia\cases</c>.</summary>
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fonia", "cases");

    /// <summary>Notes an exchange; keeps it when it is a case.</summary>
    /// <returns>The reasons it was kept, none when it went well.</returns>
    public IReadOnlyList<string> Record(ExchangeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (_gate)
        {
            var id = ++_lastId;
            _last = result;
            _lastMarked = false;

            var before = result.Before ?? new FlightContext(_callsign, FlightPhase.Parked, _airport.Airport.Ident, null);
            var reasons = CaseReasons.Of(before, result.Intent, result.Decision.Instruction);
            _lastKept = reasons.Count > 0;

            if (_lastKept)
            {
                WriteCase(id, result, reasons);
            }

            return reasons;
        }
    }

    /// <summary>The pilot says the last exchange was wrong. Returns false when there is none to mark, or it was marked already.</summary>
    public bool Mark()
    {
        lock (_gate)
        {
            if (_last is not { } last || _lastMarked)
            {
                return false;
            }

            _lastMarked = true;

            if (_lastKept)
            {
                Write(new MarkLine("mark", _lastId, _time.GetUtcNow()));
            }
            else
            {
                _lastKept = true;
                WriteCase(_lastId, last, [CaseReasons.Marked]);
            }

            return true;
        }
    }

    private void WriteCase(int id, ExchangeResult result, IReadOnlyList<string> reasons)
    {
        var before = result.Before;
        var audio = SaveAudio(id, result.Audio);

        var line = new CaseLine(
            "case",
            id,
            _time.GetUtcNow(),
            result.Station,
            before?.Phase.ToString(),
            before?.PendingReadback?.GetType().Name,
            LastTransmission(before),
            result.Heard,
            result.Intent.GetType().Name,
            result.Intent.Callsign,
            result.Decision.Instruction.GetType().Name,
            result.Reply?.Text,
            reasons,
            audio);

        if (Write(line))
        {
            _cases++;
        }
    }

    // What the controller last said before the pilot spoke: the pilot was most likely reading it back.
    private string? LastTransmission(FlightContext? before) =>
        before?.LastInstruction is { } last && _phraseology.Speak(last, new PhraseContext(before.Callsign, _airport)) is { Kind: UtteranceKind.Radio } said
            ? said.Text
            : null;

    private string? SaveAudio(int id, RecordedSpeech? audio)
    {
        if (!_saveAudio || audio is null)
        {
            return null;
        }

        var name = $"{id:000}.wav";

        try
        {
            Directory.CreateDirectory(SessionDirectory);
            File.WriteAllBytes(Path.Combine(SessionDirectory, name), WavEncoder.Encode(audio));

            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool Write(object line)
    {
        try
        {
            Directory.CreateDirectory(SessionDirectory);
            var path = Path.Combine(SessionDirectory, FileName);

            if (!_headerWritten)
            {
                File.AppendAllText(path, JsonSerializer.Serialize(new SessionLine("session", AppVersion.Current, _airport.Airport.Ident, _callsign, _startedAt, _saveAudio), Json) + "\n");
                _headerWritten = true;
            }

            File.AppendAllText(path, JsonSerializer.Serialize(line, line.GetType(), Json) + "\n");

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes the sessions older than <paramref name="maxAge"/> and then, oldest first, as many as it takes for the folder to fit in
    /// <paramref name="maxBytes"/>. Only folders named like a session are touched.
    /// </summary>
    public static void Prune(string root, TimeProvider time, TimeSpan maxAge, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(time);

        try
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            var now = time.GetUtcNow();
            var sessions = Sessions(root).OrderBy(s => s.StartedAt).ToList();

            foreach (var session in sessions.Where(s => now - s.StartedAt > maxAge).ToList())
            {
                Delete(session.Path);
                sessions.Remove(session);
            }

            var total = sessions.Sum(s => SizeOf(s.Path));

            foreach (var session in sessions)
            {
                if (total <= maxBytes)
                {
                    break;
                }

                total -= SizeOf(session.Path);
                Delete(session.Path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not cleaning up is not a failure: the next start tries again.
        }
    }

    /// <summary><c>fonia --cases</c>: where the cases are and how many each session has.</summary>
    public static void List(string root, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(output);

        var sessions = Directory.Exists(root) ? Sessions(root).OrderByDescending(s => s.StartedAt).ToList() : [];

        output.WriteLine($"Pasta dos casos: {root}");

        if (sessions.Count == 0)
        {
            output.WriteLine("Nenhum caso guardado ainda. O Fonia guarda aqui, no seu computador, o que o controlador não entendeu (e, com --save-audio, o som).");
            return;
        }

        foreach (var session in sessions.Take(20))
        {
            var cases = CountCases(session.Path);
            var audio = Directory.GetFiles(session.Path, "*.wav").Length;
            var megabytes = SizeOf(session.Path) / 1024.0 / 1024.0;

            output.WriteLine($"  {Path.GetFileName(session.Path)}: {cases} {(cases == 1 ? "caso" : "casos")}, {audio} áudio(s), {megabytes.ToString("0.0", CultureInfo.CurrentCulture)} MB");
        }
    }

    private static int CountCases(string directory)
    {
        try
        {
            var file = Path.Combine(directory, FileName);

            return File.Exists(file) ? File.ReadLines(file).Count(l => l.Contains("\"type\":\"case\"", StringComparison.Ordinal)) : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static List<(string Path, DateTimeOffset StartedAt)> Sessions(string root) =>
    [
        .. Directory.GetDirectories(root)
            .Select(path => (Path: path, Parsed: DateTimeOffset.TryParseExact(Path.GetFileName(path), SessionPattern, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at), At: at))
            .Where(s => s.Parsed)
            .Select(s => (s.Path, s.At)),
    ];

    private static long SizeOf(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use or locked: left for the next time.
        }
    }

    private sealed record SessionLine(string Type, string Version, string Airport, string Callsign, DateTimeOffset StartedAt, bool AudioSaved);

    private sealed record CaseLine(
        string Type,
        int Id,
        DateTimeOffset At,
        string Station,
        string? PhaseBefore,
        string? PendingReadback,
        string? LastTransmission,
        string Heard,
        string Intent,
        string? IntentCallsign,
        string Instruction,
        string? Reply,
        IReadOnlyList<string> Reasons,
        string? Audio);

    private sealed record MarkLine(string Type, int Id, DateTimeOffset At);
}
