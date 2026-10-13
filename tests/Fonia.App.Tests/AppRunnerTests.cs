using System.Text;
using Fonia.Core.Sim;
using Fonia.Sim.Mock;
using Fonia.Sim.Mock.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fonia.App.Tests;

public class AppRunnerTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("fonia-app-tests-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>A writer the test can read while the app is still writing to it from other threads.</summary>
    private sealed class SyncWriter : TextWriter
    {
        private readonly object _gate = new();
        private readonly StringBuilder _text = new();

        public SyncWriter()
        {
            // The same line ending on every platform, so a carriage return in the text always means "rewrite this line".
            NewLine = "\n";
        }

        public override Encoding Encoding => Encoding.UTF8;

        public string Text
        {
            get
            {
                lock (_gate)
                {
                    return _text.ToString();
                }
            }
        }

        public override void Write(char value)
        {
            lock (_gate)
            {
                _text.Append(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_gate)
            {
                _text.Append(value);
            }
        }

        public async Task WaitForAsync(string expected, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            while (!Text.Contains(expected, StringComparison.Ordinal))
            {
                await Task.Delay(5, timeout.Token);
            }
        }
    }

    private sealed class DelegateFactory(Func<AppOptions, Stream?, SimSource> create) : ISimSourceFactory
    {
        public SimSource Create(AppOptions options, Stream? replay) => create(options, replay);
    }

    private static readonly string[] ThreeRegistrations = ["PR-AAA", "PR-BBB", "PR-CCC"];

    private static AircraftState Parked(string registration = "PR-ABC") => MockAircraft.Parked() with { Registration = registration };

    private static SimSourceFactory RealFactory() => new SimSourceFactory(NullLoggerFactory.Instance, TimeProvider.System);

    private async Task<string> WriteRecordingAsync(params AircraftState[] states)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.jsonl");
        await using var file = File.Create(path);
        await using var writer = await TelemetryWriter.CreateAsync(file, leaveOpen: true, Ct);

        foreach (var state in states)
        {
            await writer.WriteAsync(state, Ct);
        }

        return path;
    }

    private static (AppRunner Runner, SyncWriter Output, SyncWriter Error) NewRunner(
        ISimSourceFactory? factory = null,
        bool interactive = false,
        ITerminal? terminal = null)
    {
        var output = new SyncWriter();
        var error = new SyncWriter();
        return (new AppRunner(factory ?? RealFactory(), output, error, interactive, terminal), output, error);
    }

    /// <summary>A runner that draws on a simulated terminal, which also answers the questions about its size.</summary>
    private static AppRunner ScreenRunner(ISimSourceFactory factory, FakeScreen screen)
    {
        return new AppRunner(factory, screen, new SyncWriter(), interactive: true, terminal: screen);
    }

    /// <summary>The lines the display wrote in place: what follows each carriage return, up to its line feed.</summary>
    private static List<string> RewrittenLines(string text)
    {
        return [.. text.Split('\r').Skip(1).Select(segment => segment.Split('\n')[0])];
    }

    // ---- replay ----

    [Fact]
    public async Task A_replay_shows_the_recording_and_ends_by_itself()
    {
        var script = MockAircraft.TaxiSequence(5);
        var path = await WriteRecordingAsync([.. script]);
        var (runner, output, error) = NewRunner();

        var exit = await runner.RunAsync(new AppOptions(SimSourceKind.Replay, path, Speed: double.PositiveInfinity), Ct).WaitAsync(Timeout, Ct);

        exit.ShouldBe(0);
        error.Text.ShouldBeEmpty();
        output.Text.ShouldContain(StateFormatter.Format(script[^1]));
        output.Text.ShouldContain("Reprodução concluída.");

        // The closing message comes after the last state line, not before it.
        output.Text.LastIndexOf(StateFormatter.Format(script[^1]), StringComparison.Ordinal)
            .ShouldBeLessThan(output.Text.IndexOf("Reprodução concluída.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_replay_file_that_does_not_exist_is_a_usage_error()
    {
        var (runner, output, error) = NewRunner();

        var exit = await runner.RunAsync(new AppOptions(SimSourceKind.Replay, Path.Combine(_directory, "nothing.jsonl")), Ct);

        exit.ShouldBe(2);
        error.Text.ShouldContain("não encontrado");
        output.Text.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_corrupt_replay_file_fails_with_the_line_number()
    {
        var path = await WriteRecordingAsync(Parked());
        await File.AppendAllTextAsync(path, "{ broken\n", Ct);
        var (runner, output, error) = NewRunner();

        var exit = await runner.RunAsync(new AppOptions(SimSourceKind.Replay, path, Speed: double.PositiveInfinity), Ct).WaitAsync(Timeout, Ct);

        exit.ShouldBe(1);
        error.Text.ShouldContain("line 3");
        output.Text.ShouldContain(StateFormatter.Format(Parked())); // what was valid before the problem was shown
    }

    // ---- mock source and recording ----

    [Fact]
    public async Task The_mock_source_shows_a_simulated_taxi_until_the_duration_ends()
    {
        var (runner, output, error) = NewRunner();

        var exit = await runner.RunAsync(new AppOptions(SimSourceKind.Mock, Duration: TimeSpan.FromMilliseconds(400)), Ct).WaitAsync(Timeout, Ct);

        exit.ShouldBe(0);
        error.Text.ShouldBeEmpty();
        output.Text.ShouldContain("PR-ABC");
        output.Text.ShouldContain("Desconectado.");
    }

    [Fact]
    public async Task Recording_writes_a_valid_file_with_what_was_shown()
    {
        var path = Path.Combine(_directory, "session.jsonl");
        var (runner, _, _) = NewRunner();

        var exit = await runner.RunAsync(new AppOptions(SimSourceKind.Mock, RecordPath: path, Duration: TimeSpan.FromMilliseconds(400)), Ct).WaitAsync(Timeout, Ct);

        exit.ShouldBe(0);
        var recorded = new List<AircraftState>();
        await using var file = File.OpenRead(path);
        await foreach (var state in TelemetryReader.ReadAsync(file, Ct))
        {
            recorded.Add(state);
        }

        recorded.ShouldNotBeEmpty();
        recorded.ShouldAllBe(s => s.Registration == "PR-ABC");
    }

    [Fact]
    public async Task A_record_path_that_cannot_be_written_is_an_error_not_a_crash()
    {
        var (runner, _, error) = NewRunner();
        var options = new AppOptions(SimSourceKind.Mock, RecordPath: Path.Combine(_directory, "missing-folder", "x.jsonl"), Duration: TimeSpan.FromMilliseconds(200));

        var exit = await runner.RunAsync(options, Ct).WaitAsync(Timeout, Ct);

        exit.ShouldBe(1);
        error.Text.ShouldContain("missing-folder");
    }

    [Fact]
    public async Task Cancelling_ends_cleanly_and_leaves_the_recording_valid()
    {
        var path = Path.Combine(_directory, "cancelled.jsonl");
        var (runner, output, _) = NewRunner();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = runner.RunAsync(new AppOptions(SimSourceKind.Mock, RecordPath: path), cts.Token);
        await output.WaitForAsync("PR-ABC", Ct);
        await cts.CancelAsync();
        var exit = await run.WaitAsync(Timeout, Ct);

        exit.ShouldBe(0);
        await using var file = File.OpenRead(path);
        var states = new List<AircraftState>();
        await foreach (var state in TelemetryReader.ReadAsync(file, Ct))
        {
            states.Add(state);
        }

        states.ShouldNotBeEmpty();
    }

    // ---- connection messages and the line display ----

    private static SimSource ScriptedSource(MockSimAdapter sim, Func<MockSimAdapter, CancellationToken, Task> drive)
    {
        return new SimSource(sim, ct => drive(sim, ct), EndsWhenDriveCompletes: true, Description: "teste");
    }

    [Fact]
    public async Task Connection_changes_are_announced_in_order()
    {
        var sim = new MockSimAdapter();
        var factory = new DelegateFactory((_, _) => ScriptedSource(sim, (s, _) =>
        {
            s.SetState(Parked());
            s.SimulateConnectionLoss();
            s.RestoreConnection();
            return Task.CompletedTask;
        }));
        var (runner, output, _) = NewRunner(factory);

        var exit = await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        exit.ShouldBe(0);
        var text = output.Text;
        var order = new[] { "Conectando", "Conectado.", "perdida", "Reconectado.", "Desconectado." };
        var last = -1;

        foreach (var phrase in order)
        {
            var at = text.IndexOf(phrase, last + 1, StringComparison.Ordinal);
            at.ShouldBeGreaterThan(last, $"'{phrase}' is missing or out of order in:\n{text}");
            last = at;
        }
    }

    [Fact]
    public async Task The_adapter_is_disconnected_when_the_app_ends()
    {
        var sim = new MockSimAdapter();
        var factory = new DelegateFactory((_, _) => ScriptedSource(sim, (s, _) =>
        {
            s.SetState(Parked());
            return Task.CompletedTask;
        }));
        var (runner, _, _) = NewRunner(factory);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        sim.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
    }

    [Fact]
    public async Task Without_a_terminal_every_snapshot_is_its_own_line()
    {
        var sim = new MockSimAdapter();
        var (runner, output, _) = NewRunner(new DelegateFactory((_, _) => ScriptedSource(sim, async (s, ct) =>
        {
            // Let the display write each snapshot before the next one replaces it.
            s.SetState(Parked("PR-AAA"));
            await Task.Delay(100, ct);
            s.SetState(Parked("PR-BBB"));
            await Task.Delay(100, ct);
        })));

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        var lines = output.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.ShouldContain(l => l.Contains("PR-AAA", StringComparison.Ordinal));
        lines.ShouldContain(l => l.Contains("PR-BBB", StringComparison.Ordinal));
        output.Text.ShouldNotContain("\r");
    }

    [Fact]
    public async Task On_a_terminal_the_state_line_is_rewritten_in_place_and_messages_start_on_a_new_line()
    {
        var sim = new MockSimAdapter();
        var (runner, output, _) = NewRunner(
            new DelegateFactory((_, _) => ScriptedSource(sim, async (s, ct) =>
            {
                s.SetState(Parked("PR-AAA"));
                await Task.Delay(100, ct);
                s.SetState(Parked("PR-BBB"));
                await Task.Delay(100, ct);
            })),
            interactive: true);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        var text = output.Text;
        text.ShouldContain("\r"); // the line is rewritten with a carriage return

        // The two snapshots share one screen line: no line feed between them.
        var first = text.IndexOf("PR-AAA", StringComparison.Ordinal);
        var second = text.IndexOf("PR-BBB", StringComparison.Ordinal);
        first.ShouldBeGreaterThanOrEqualTo(0);
        second.ShouldBeGreaterThan(first);
        text[first..second].ShouldNotContain("\n");

        // The closing message is not glued to the end of the state line.
        var lastState = text.LastIndexOf("XPDR", StringComparison.Ordinal);
        var closing = text.LastIndexOf("Desconectado.", StringComparison.Ordinal);
        closing.ShouldBeGreaterThan(lastState);
        text[lastState..closing].ShouldContain("\n");
    }

    [Fact]
    public async Task Redirected_output_is_never_shortened_whatever_the_width()
    {
        var sim = new MockSimAdapter();
        var (runner, output, _) = NewRunner(
            new DelegateFactory((_, _) => ScriptedSource(sim, (s, _) =>
            {
                s.SetState(Parked());
                return Task.CompletedTask;
            })),
            interactive: false,
            terminal: new FakeScreen(40));

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        output.Text.ShouldContain(StateFormatter.Format(Parked()));
    }

    // ---- one line, whatever the window does ----

    private static async Task WaitForRowAsync(FakeScreen screen, string text, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        while (!screen.Rows.Any(row => row.Contains(text, StringComparison.Ordinal)))
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    /// <summary>Plays states into the display, waiting for each to be drawn; <paramref name="between"/> runs after each one (resize the window, for instance).</summary>
    private static DelegateFactory Playing(MockSimAdapter sim, FakeScreen screen, params (string Registration, Action? Then)[] steps)
    {
        return new DelegateFactory((_, _) => ScriptedSource(sim, async (s, ct) =>
        {
            foreach (var (registration, then) in steps)
            {
                s.SetState(Parked(registration));
                await WaitForRowAsync(screen, registration, ct);
                then?.Invoke();
            }
        }));
    }

    // The rows of the screen on one line, for failure messages.
    private static string Describe(FakeScreen screen) => string.Join(" | ", screen.Rows);

    // A state line starts with the time of the snapshot; a status message starts with "[time]" and a banner with "Fonia".
    private static readonly System.Text.RegularExpressions.Regex StateRowStart = new(@"^\d{2}:\d{2}:\d{2}  ", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The screen shows exactly one state line, it is the latest one, it is not wider than the window, and nothing
    /// of any earlier state line is left anywhere (neither the first nor a wrapped continuation of it).
    /// </summary>
    private static void AssertSingleStateRow(FakeScreen screen, string registration, int maxLength)
    {
        var rows = screen.Rows;
        var shown = Describe(screen);
        var stateRows = rows.Where(row => StateRowStart.IsMatch(row)).ToList();

        stateRows.Count.ShouldBe(1, $"expected one state row. The screen was: {shown}");
        stateRows[0].ShouldContain(registration, customMessage: $"the state row is not the latest. The screen was: {shown}");
        stateRows[0].Length.ShouldBeLessThanOrEqualTo(maxLength, $"the state row is wider than the window. The screen was: {shown}");

        var others = rows.Where(row => !ReferenceEquals(row, stateRows[0])).ToList();
        others.ShouldNotContain(
            row => row.Contains("HDG", StringComparison.Ordinal) || row.Contains("XPDR", StringComparison.Ordinal)
                || row.Contains("COM1", StringComparison.Ordinal) || row.Contains("AGL", StringComparison.Ordinal)
                || row.Contains(" GS ", StringComparison.Ordinal) || row.Contains("PR-AAA", StringComparison.Ordinal),
            $"a piece of an old state line is left on the screen. The screen was: {shown}");
    }

    [Fact]
    public async Task Shrinking_the_window_while_running_keeps_the_state_on_one_screen_row()
    {
        // The reported bug: the line was drawn in a wide window; shrinking it made the terminal wrap that line onto
        // two rows, and the next update was drawn on the second one, leaving the first behind as a new line.
        var screen = new FakeScreen(200);
        var sim = new MockSimAdapter();
        var runner = ScreenRunner(Playing(sim, screen, ("PR-AAA", () => screen.Resize(70)), ("PR-BBB", null)), screen);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        AssertSingleStateRow(screen, "PR-BBB", maxLength: 69);
    }

    [Fact]
    public async Task Shrinking_the_window_several_times_never_leaves_old_lines_behind()
    {
        var screen = new FakeScreen(200);
        var sim = new MockSimAdapter();
        var runner = ScreenRunner(
            Playing(
                sim,
                screen,
                ("PR-AAA", () => screen.Resize(120)),
                ("PR-BBB", () => screen.Resize(60)),
                ("PR-CCC", () => screen.Resize(25)),
                ("PR-DDD", null)),
            screen);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        AssertSingleStateRow(screen, "PR-DDD", maxLength: 24);
    }

    [Fact]
    public async Task Growing_the_window_again_still_keeps_one_row()
    {
        var screen = new FakeScreen(60);
        var sim = new MockSimAdapter();
        var runner = ScreenRunner(
            Playing(sim, screen, ("PR-AAA", () => screen.Resize(200)), ("PR-BBB", () => screen.Resize(60)), ("PR-CCC", null)),
            screen);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        AssertSingleStateRow(screen, "PR-CCC", maxLength: 59);
    }

    [Fact]
    public async Task A_window_that_still_fits_the_line_after_a_resize_changes_nothing_on_screen()
    {
        var screen = new FakeScreen(200);
        var sim = new MockSimAdapter();
        var runner = ScreenRunner(Playing(sim, screen, ("PR-AAA", () => screen.Resize(150)), ("PR-BBB", null)), screen);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        AssertSingleStateRow(screen, "PR-BBB", maxLength: 149);
        screen.Rows.ShouldContain(row => row.Contains("COM1 121.900/118.100", StringComparison.Ordinal)); // the full layout still fits
    }

    [Fact]
    public async Task The_state_never_scrolls_above_the_banner_and_messages_that_were_already_on_screen()
    {
        var screen = new FakeScreen(200);
        var sim = new MockSimAdapter();
        var runner = ScreenRunner(Playing(sim, screen, ("PR-AAA", () => screen.Resize(40)), ("PR-BBB", () => screen.Resize(30)), ("PR-CCC", null)), screen);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        // Moving the cursor up to erase the wrapped rows must not eat what is above the state line.
        screen.Rows[0].ShouldStartWith("Fonia -");
        screen.Rows.Count(row => row.StartsWith('[')).ShouldBeGreaterThanOrEqualTo(2); // Conectando, Conectado
    }

    [Fact]
    public async Task The_banner_says_where_the_data_comes_from()
    {
        var (runner, output, _) = NewRunner();

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock, Duration: TimeSpan.FromMilliseconds(150)), Ct).WaitAsync(Timeout, Ct);

        output.Text.ShouldStartWith("Fonia");
        output.Text.ShouldContain("Ctrl+C");
    }
}
