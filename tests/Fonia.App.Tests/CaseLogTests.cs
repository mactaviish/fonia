using System.Text.Json;
using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Audio;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Pipeline;
using Fonia.Core.Sim;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.App.Tests;

public sealed class CaseLogTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 20, 0, 0, TimeSpan.Zero);

    private static readonly Airport Curitiba = new(
        "SBCT", "Curitiba", "Curitiba", AirportType.Large, -25.5, -49.1, 2000, "SBCT", null, "Curitiba", SpokenNameSource.Reference, true,
        [new Runway(new RunwayEnd("15", 150, null, null), new RunwayEnd("33", 330, null, null), 6000, 150, "ASP", true, false)],
        [new AirportFrequency(AirportFrequencyType.Ground, new Frequency(121900), "Ground")]);

    private static readonly AirportContext Solo = AirportContext.Create(Curitiba);

    private static readonly FlightContext Parked = new("PRFRA", FlightPhase.Parked, "SBCT", null);

    private static readonly FlightContext Taxiing = new("PRFRA", FlightPhase.Taxiing, "SBCT", new TaxiClearance("15"), new TaxiClearance("15"));

    private static readonly StageTimings NoTime = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fonia-cases-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(Start);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private CaseLog Create(bool saveAudio = false) => new(_root, _time, new PortuguesePhraseology(), Solo, "PR-FRA", saveAudio);

    private static ExchangeResult Exchange(FlightContext before, Intent intent, AtcInstruction instruction, string heard, RecordedSpeech? audio = null) =>
        new(heard, intent, new AtcDecision(instruction, before), new PortuguesePhraseology().Speak(instruction, new PhraseContext("PRFRA", Solo)), null, NoTime, "SOLO Curitiba", before, audio);

    private static ExchangeResult NotUnderstood(string heard = "blá blá", RecordedSpeech? audio = null) =>
        Exchange(Parked, new Unknown(heard), new SayAgainRequest(), heard, audio);

    private static ExchangeResult WentWell() =>
        Exchange(Parked, new RequestStartup("PRFRA", null), new StartupApproved(), "solicito acionamento");

    private static RecordedSpeech Speech() => new(AudioFormat.Speech, new byte[AudioFormat.Speech.BytesPerSecond]);

    private string[] Sessions() => Directory.Exists(_root) ? Directory.GetDirectories(_root) : [];

    private static List<JsonElement> Lines(CaseLog log) =>
        [.. File.ReadAllLines(Path.Combine(log.SessionDirectory, "cases.jsonl")).Select(l => JsonDocument.Parse(l).RootElement.Clone())];

    [Fact]
    public void An_exchange_that_went_well_writes_nothing_and_creates_no_folder()
    {
        var log = Create();

        log.Record(WentWell()).ShouldBeEmpty();

        Sessions().ShouldBeEmpty();
        log.Cases.ShouldBe(0);
    }

    [Fact]
    public void A_case_is_written_with_what_was_heard_what_was_understood_and_what_the_controller_said()
    {
        var log = Create();

        log.Record(Exchange(Taxiing, new Readback("PRFRA", "pista dois nove"), new RequestReadbackCorrection(new TaxiClearance("15")), "pista dois nove")).ShouldBe([CaseReasons.Corrected]);

        var lines = Lines(log);
        lines.Count.ShouldBe(2);

        lines[0].GetProperty("type").GetString().ShouldBe("session");
        lines[0].GetProperty("airport").GetString().ShouldBe("SBCT");
        lines[0].GetProperty("callsign").GetString().ShouldBe("PR-FRA");
        lines[0].GetProperty("startedAt").GetDateTimeOffset().ShouldBe(Start);

        var entry = lines[1];
        entry.GetProperty("type").GetString().ShouldBe("case");
        entry.GetProperty("id").GetInt32().ShouldBe(1);
        entry.GetProperty("heard").GetString().ShouldBe("pista dois nove");
        entry.GetProperty("intent").GetString().ShouldBe("Readback");
        entry.GetProperty("instruction").GetString().ShouldBe("RequestReadbackCorrection");
        entry.GetProperty("reply").GetString()!.ShouldContain("negativo");
        entry.GetProperty("station").GetString().ShouldBe("SOLO Curitiba");
        entry.GetProperty("phaseBefore").GetString().ShouldBe("Taxiing");
        entry.GetProperty("pendingReadback").GetString().ShouldBe("TaxiClearance");
        entry.GetProperty("lastTransmission").GetString()!.ShouldContain("PISTA UNO CINCO");
        entry.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ShouldBe([CaseReasons.Corrected]);
        entry.GetProperty("audio").ValueKind.ShouldBe(JsonValueKind.Null);
        log.Cases.ShouldBe(1);
    }

    [Fact]
    public void The_ids_count_every_exchange_not_only_the_cases()
    {
        var log = Create();

        log.Record(WentWell());
        log.Record(NotUnderstood());

        Lines(log)[1].GetProperty("id").GetInt32().ShouldBe(2);
    }

    [Fact]
    public void The_sound_is_kept_only_when_the_user_allowed_it()
    {
        var without = Create(saveAudio: false);
        without.Record(NotUnderstood(audio: Speech()));
        Directory.GetFiles(without.SessionDirectory, "*.wav").ShouldBeEmpty();

        _time.Advance(TimeSpan.FromMinutes(5));
        var with = Create(saveAudio: true);
        with.Record(NotUnderstood(audio: Speech()));

        var wav = Directory.GetFiles(with.SessionDirectory, "*.wav").ShouldHaveSingleItem();
        Path.GetFileName(wav).ShouldBe("001.wav");
        File.ReadAllBytes(wav).Take(4).ShouldBe("RIFF"u8.ToArray());
        Lines(with)[1].GetProperty("audio").GetString().ShouldBe("001.wav");
        Lines(with)[0].GetProperty("audioSaved").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Marking_a_case_that_was_kept_adds_a_mark_to_it()
    {
        var log = Create();
        log.Record(NotUnderstood());

        log.Mark().ShouldBeTrue();

        var mark = Lines(log)[2];
        mark.GetProperty("type").GetString().ShouldBe("mark");
        mark.GetProperty("id").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void Marking_an_exchange_that_went_well_keeps_it_as_a_case_with_its_sound()
    {
        var log = Create(saveAudio: true);
        log.Record(Exchange(Parked, new RequestStartup("PRFRA", null), new StartupApproved(), "solicito acionamento", Speech()));
        Sessions().ShouldBeEmpty();

        log.Mark().ShouldBeTrue();

        var entry = Lines(log)[1];
        entry.GetProperty("type").GetString().ShouldBe("case");
        entry.GetProperty("id").GetInt32().ShouldBe(1);
        entry.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ShouldBe([CaseReasons.Marked]);
        entry.GetProperty("audio").GetString().ShouldBe("001.wav");
        File.Exists(Path.Combine(log.SessionDirectory, "001.wav")).ShouldBeTrue();
        log.Cases.ShouldBe(1);
    }

    [Fact]
    public void Marking_the_same_exchange_twice_marks_it_once_and_marking_before_anything_was_said_is_nothing()
    {
        var log = Create();
        log.Mark().ShouldBeFalse();

        log.Record(WentWell());
        log.Mark().ShouldBeTrue();
        log.Mark().ShouldBeFalse();

        Lines(log).Count(l => l.GetProperty("type").GetString() != "session").ShouldBe(1);
    }

    [Fact]
    public void An_exchange_that_failed_in_the_disk_does_not_stop_the_radio()
    {
        File.WriteAllText(_root, "this is a file where the folder should be");

        var log = Create();

        Should.NotThrow(() => log.Record(NotUnderstood()));
        Should.NotThrow(() => log.Mark());
    }

    [Fact]
    public void The_sessions_are_told_apart_by_the_time_they_began()
    {
        var first = Create();
        _time.Advance(TimeSpan.FromMinutes(5));
        var second = Create();

        first.SessionDirectory.ShouldNotBe(second.SessionDirectory);
        Path.GetFileName(first.SessionDirectory).ShouldBe("20261015-200000");
    }

    private string MakeSession(string name, int kilobytes)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "001.wav"), new byte[kilobytes * 1024]);
        File.WriteAllText(Path.Combine(directory, "cases.jsonl"), "{}");
        return directory;
    }

    [Fact]
    public void Old_sessions_are_removed_and_the_recent_ones_stay()
    {
        var old = MakeSession("20260901-100000", 10);
        var recent = MakeSession("20261010-100000", 10);

        CaseLog.Prune(_root, _time, TimeSpan.FromDays(30), long.MaxValue);

        Directory.Exists(old).ShouldBeFalse();
        Directory.Exists(recent).ShouldBeTrue();
    }

    [Fact]
    public void When_the_folder_is_too_big_the_oldest_sessions_go_first()
    {
        var oldest = MakeSession("20261001-100000", 400);
        var middle = MakeSession("20261005-100000", 400);
        var newest = MakeSession("20261010-100000", 400);

        CaseLog.Prune(_root, _time, TimeSpan.FromDays(30), 900 * 1024);

        Directory.Exists(oldest).ShouldBeFalse();
        Directory.Exists(middle).ShouldBeTrue();
        Directory.Exists(newest).ShouldBeTrue();
    }

    [Fact]
    public void A_folder_that_is_not_a_session_is_never_touched()
    {
        var other = Path.Combine(_root, "my-notes");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "a.txt"), "x");

        CaseLog.Prune(_root, _time, TimeSpan.Zero, 0);

        File.Exists(Path.Combine(other, "a.txt")).ShouldBeTrue();
    }

    [Fact]
    public void Pruning_a_folder_that_does_not_exist_is_nothing()
    {
        Should.NotThrow(() => CaseLog.Prune(_root, _time, TimeSpan.FromDays(30), 1));
    }

    [Fact]
    public void The_list_says_where_the_cases_are_and_how_many_each_session_has()
    {
        var log = Create();
        log.Record(NotUnderstood());
        log.Record(NotUnderstood("outra"));

        var output = new StringWriter();
        CaseLog.List(_root, output);

        output.ToString().ShouldContain(_root);
        output.ToString().ShouldContain("20261015-200000");
        output.ToString().ShouldContain("2 casos");
    }

    [Fact]
    public void With_no_cases_the_list_says_so()
    {
        var output = new StringWriter();

        CaseLog.List(_root, output);

        output.ToString().ShouldContain("Nenhum caso");
    }

    [Fact]
    public void A_new_flight_is_a_line_in_the_same_file_and_what_was_said_before_is_not_marked_in_it()
    {
        var log = Create();
        log.Record(NotUnderstood());

        log.StartFlight(Solo, "PT-XYZ");

        log.Mark().ShouldBeFalse();
        var lines = Lines(log);
        lines[^1].GetProperty("type").GetString().ShouldBe("flight");
        lines[^1].GetProperty("airport").GetString().ShouldBe("SBCT");
        lines[^1].GetProperty("callsign").GetString().ShouldBe("PT-XYZ");
    }
}
