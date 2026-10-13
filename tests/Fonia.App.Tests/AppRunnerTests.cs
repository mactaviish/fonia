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

    // ---- the state is a block of rows that is rewritten in place, whatever the window does ----

    private static async Task WaitForRowAsync(FakeScreen screen, string text, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        while (!screen.Rows.Any(row => row.Contains(text, StringComparison.Ordinal)))
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    // The rows of the screen on one line, for failure messages.
    private static string Describe(FakeScreen screen) => string.Join(" | ", screen.Rows);

    private static readonly string[] StateTokens = ["HDG", "XPDR", "COM1", "COM2", " GS ", "AGL", "PR-"];

    /// <summary>
    /// The screen shows the latest state laid out for <paramref name="columns"/> (as consecutive rows) and nothing of
    /// any earlier state is left anywhere, neither a whole row nor a wrapped piece of one.
    /// </summary>
    private static void AssertShowsOnly(FakeScreen screen, string registration, int columns)
    {
        var rows = screen.Rows;
        var expected = StateFormatter.FormatRows(Parked(registration), columns);
        var shown = Describe(screen);

        var start = -1;
        for (var i = 0; i + expected.Count <= rows.Count && start < 0; i++)
        {
            if (expected.Select((row, k) => rows[i + k] == row).All(equal => equal))
            {
                start = i;
            }
        }

        start.ShouldBeGreaterThanOrEqualTo(0, $"the latest state ({string.Join(" | ", expected)}) is not on the screen as consecutive rows. The screen was: {shown}");

        var others = rows.Where((_, index) => index < start || index >= start + expected.Count).ToList();
        others.ShouldNotContain(
            row => StateTokens.Any(token => row.Contains(token, StringComparison.Ordinal)),
            $"a piece of an old state is left on the screen. The screen was: {shown}");
    }

    /// <summary>Plays states into the display, waiting for each one to be drawn; the action runs after each (resize the window, for instance).</summary>
    private static DelegateFactory Playing(MockSimAdapter sim, FakeScreen screen, params (string Registration, Action? Then)[] steps)
    {
        return new DelegateFactory((_, _) => ScriptedSource(sim, async (s, ct) =>
        {
            foreach (var (registration, then) in steps)
            {
                s.SetState(Parked(registration));
                await WaitForRowAsync(screen, registration, ct);
                await Task.Delay(30, ct); // the whole block is written under one lock; give it a moment
                then?.Invoke();
            }
        }));
    }

    [Theory]
    [InlineData(200)] // one row
    [InlineData(80)]  // a few rows
    [InlineData(40)]
    public async Task A_new_snapshot_never_adds_a_line_to_the_screen(int width)
    {
        var screen = new FakeScreen(width);
        var sim = new MockSimAdapter();
        var heights = new List<int>();
        var runner = ScreenRunner(
            Playing(
                sim,
                screen,
                ("PR-AAA", () => heights.Add(screen.Rows.Count)),
                ("PR-BBB", () => heights.Add(screen.Rows.Count)),
                ("PR-CCC", () => heights.Add(screen.Rows.Count)),
                ("PR-DDD", () => heights.Add(screen.Rows.Count)),
                ("PR-EEE", () => heights.Add(screen.Rows.Count))),
            screen);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        heights.Distinct().Count().ShouldBe(1, $"the screen grew with the updates: {string.Join(", ", heights)}. Final screen: {Describe(screen)}");
        AssertShowsOnly(screen, "PR-EEE", columns: width - 1);
    }

    [Fact]
    public async Task In_a_narrow_window_everything_is_still_shown_over_a_few_rows()
    {
        var screen = new FakeScreen(60);
        var sim = new MockSimAdapter();
        var runner = ScreenRunner(Playing(sim, screen, ("PR-AAA", null)), screen);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        var block = StateFormatter.FormatRows(Parked("PR-AAA"), 59);
        block.Count.ShouldBeGreaterThan(1);
        string.Join("  ", block).ShouldBe(StateFormatter.Format(Parked("PR-AAA"))); // nothing was dropped
        AssertShowsOnly(screen, "PR-AAA", columns: 59);
    }

    [Fact]
    public async Task Shrinking_the_window_while_running_keeps_one_block_in_the_same_place()
    {
        // The reported bug: a line drawn in a wide window was wrapped by the terminal when the window shrank, and the
        // next update appeared below it, as a new line, leaving the old one behind.
        var screen = new FakeScreen(200);
        var sim = new MockSimAdapter();
        var runner = ScreenRunner(Playing(sim, screen, ("PR-AAA", () => screen.Resize(70)), ("PR-BBB", null)), screen);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        AssertShowsOnly(screen, "PR-BBB", columns: 69);
    }

    [Fact]
    public async Task Shrinking_the_window_several_times_never_leaves_old_text_behind()
    {
        var screen = new FakeScreen(200);
        var sim = new MockSimAdapter();
        var runner = ScreenRunner(
            Playing(
                sim,
                screen,
                ("PR-AAA", () => screen.Resize(120)),
                ("PR-BBB", () => screen.Resize(60)),
                ("PR-CCC", () => screen.Resize(30)),
                ("PR-DDD", null)),
            screen);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        AssertShowsOnly(screen, "PR-DDD", columns: 29);
    }

    [Fact]
    public async Task Growing_and_shrinking_the_window_again_still_leaves_one_block()
    {
        var screen = new FakeScreen(60);
        var sim = new MockSimAdapter();
        var runner = ScreenRunner(
            Playing(sim, screen, ("PR-AAA", () => screen.Resize(200)), ("PR-BBB", () => screen.Resize(60)), ("PR-CCC", () => screen.Resize(90)), ("PR-DDD", null)),
            screen);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        AssertShowsOnly(screen, "PR-DDD", columns: 89);
    }

    [Fact]
    public async Task A_resize_that_still_fits_the_state_changes_nothing_on_screen()
    {
        var screen = new FakeScreen(200);
        var sim = new MockSimAdapter();
        var runner = ScreenRunner(Playing(sim, screen, ("PR-AAA", () => screen.Resize(150)), ("PR-BBB", null)), screen);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        AssertShowsOnly(screen, "PR-BBB", columns: 149);
        StateFormatter.FormatRows(Parked("PR-BBB"), 149).Count.ShouldBe(1); // still a single row
    }

    [Fact]
    public async Task Redrawing_the_block_never_touches_what_was_printed_above_it()
    {
        var screen = new FakeScreen(200);
        var sim = new MockSimAdapter();
        var runner = ScreenRunner(Playing(sim, screen, ("PR-AAA", () => screen.Resize(40)), ("PR-BBB", () => screen.Resize(30)), ("PR-CCC", null)), screen);

        await runner.RunAsync(new AppOptions(SimSourceKind.Mock), Ct).WaitAsync(Timeout, Ct);

        // Moving the cursor up to erase the old rows must not eat the banner and the messages above the state.
        var text = string.Concat(screen.Rows).Replace(" ", string.Empty, StringComparison.Ordinal);
        text.ShouldStartWith("Fonia-teste.Ctrl+Cparasair.");
        text.ShouldContain("Conectandoaosimulador...");
        text.ShouldContain("Conectado.");
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
