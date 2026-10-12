using System.Text;
using Fonia.Core.Sim;
using Fonia.Sim.Mock;
using Fonia.Sim.Mock.Telemetry;

namespace Fonia.Core.Tests.Telemetry;

public class TelemetryFileTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<MemoryStream> WriteAsync(params AircraftState[] states)
    {
        var stream = new MemoryStream();
        await using (var writer = await TelemetryWriter.CreateAsync(stream, leaveOpen: true, Ct))
        {
            foreach (var state in states)
            {
                await writer.WriteAsync(state, Ct);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream FromText(string text) => new(new UTF8Encoding(false).GetBytes(text));

    private static async Task<List<AircraftState>> ReadAllAsync(Stream stream)
    {
        var states = new List<AircraftState>();
        await foreach (var state in TelemetryReader.ReadAsync(stream, Ct))
        {
            states.Add(state);
        }

        return states;
    }

    private static string Text(MemoryStream stream) => Encoding.UTF8.GetString(stream.ToArray());

    [Fact]
    public async Task Writer_starts_with_a_header_and_writes_one_line_per_snapshot()
    {
        using var stream = await WriteAsync(MockAircraft.TaxiSequence(3).ToArray());

        var lines = Text(stream).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBe(4);
        lines[0].ShouldBe("{\"format\":\"fonia-telemetry\",\"version\":1}");
        lines.Skip(1).ShouldAllBe(l => l.StartsWith('{') && l.EndsWith('}'));
    }

    [Fact]
    public async Task Writer_uses_utf8_without_bom_and_unix_line_endings()
    {
        using var stream = await WriteAsync(MockAircraft.Parked());

        var bytes = stream.ToArray();

        bytes[0].ShouldBe((byte)'{');
        Text(stream).ShouldNotContain("\r");
        Text(stream).ShouldEndWith("\n");
    }

    [Fact]
    public async Task Frequencies_are_stored_as_integer_khz()
    {
        using var stream = await WriteAsync(MockAircraft.Parked() with
        {
            Com1 = new RadioState(Frequency.FromMhz(122.8), Frequency.FromMhz(124.85)),
        });

        Text(stream).ShouldContain("\"active\":122800");
        Text(stream).ShouldContain("\"standby\":124850");
    }

    [Fact]
    public async Task Snapshots_survive_a_round_trip_unchanged()
    {
        var original = MockAircraft.TaxiSequence(5).ToArray();
        original[2] = original[2] with
        {
            LatitudeDegrees = -27.132384729,
            LongitudeDegrees = -52.660612345,
            AltitudeMslFeet = 2123.4,
            Timestamp = new DateTimeOffset(2026, 10, 12, 19, 43, 8, 591, TimeSpan.FromHours(-3)),
            Com1 = new RadioState(Frequency.FromMhz(130.85), Frequency.FromMhz(124.85)),
            TransponderCode = 7000,
            OnGround = false,
        };

        using var stream = await WriteAsync(original);
        var read = await ReadAllAsync(stream);

        read.ShouldBe(original);
        read[2].Timestamp.Offset.ShouldBe(TimeSpan.FromHours(-3));
    }

    [Fact]
    public async Task A_radio_that_reports_no_frequency_survives_a_round_trip_as_null()
    {
        var original = MockAircraft.Parked() with { Com2 = null };

        using var stream = await WriteAsync(original);
        var text = Text(stream);
        var read = await ReadAllAsync(stream);

        text.ShouldContain("\"com2\":null");
        read.ShouldBe([original]);
        read[0].Com2.ShouldBeNull();
    }

    [Fact]
    public async Task Non_ascii_text_survives_a_round_trip()
    {
        var original = MockAircraft.Parked() with { Registration = "PR-ÁÇÃ", AircraftType = "Citação" };

        using var stream = await WriteAsync(original);

        (await ReadAllAsync(stream)).ShouldBe([original]);
    }

    [Fact]
    public async Task A_recording_without_snapshots_is_valid_and_empty()
    {
        using var stream = await WriteAsync();

        (await ReadAllAsync(stream)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Writer_can_leave_the_stream_open_or_close_it()
    {
        var kept = new MemoryStream();
        await (await TelemetryWriter.CreateAsync(kept, leaveOpen: true, Ct)).DisposeAsync();
        kept.CanWrite.ShouldBeTrue();

        var closed = new MemoryStream();
        await (await TelemetryWriter.CreateAsync(closed, leaveOpen: false, Ct)).DisposeAsync();
        closed.CanWrite.ShouldBeFalse();
    }

    [Fact]
    public async Task Reader_ignores_blank_lines_and_unknown_properties()
    {
        using var written = await WriteAsync(MockAircraft.Parked());
        var lines = Text(written).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var withExtras = lines[1].Replace("{", "{\"futureField\":42,", StringComparison.Ordinal);
        using var stream = FromText($"{lines[0]}\n\n{withExtras}\n\n");

        (await ReadAllAsync(stream)).ShouldBe([MockAircraft.Parked()]);
    }

    [Fact]
    public async Task Reader_rejects_an_empty_file()
    {
        using var stream = FromText(string.Empty);

        var error = await Should.ThrowAsync<TelemetryFormatException>(() => ReadAllAsync(stream));

        error.Message.ShouldContain("header");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"format\":\"something-else\",\"version\":1}")]
    [InlineData("{\"version\":1}")]
    public async Task Reader_rejects_a_missing_or_foreign_header(string header)
    {
        using var stream = FromText(header + "\n");

        var error = await Should.ThrowAsync<TelemetryFormatException>(() => ReadAllAsync(stream));

        error.LineNumber.ShouldBe(1);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData(0)]
    public async Task Reader_rejects_an_unsupported_version_with_a_clear_message(int version)
    {
        using var stream = FromText($"{{\"format\":\"fonia-telemetry\",\"version\":{version}}}\n");

        var error = await Should.ThrowAsync<TelemetryFormatException>(() => ReadAllAsync(stream));

        error.Message.ShouldContain($"version {version}");
        error.Message.ShouldContain("supports version 1");
    }

    [Fact]
    public async Task Reader_reports_the_line_number_of_a_malformed_snapshot()
    {
        using var written = await WriteAsync(MockAircraft.TaxiSequence(2).ToArray());
        var lines = Text(written).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var stream = FromText($"{lines[0]}\n{lines[1]}\n{{ broken\n{lines[2]}\n");

        var error = await Should.ThrowAsync<TelemetryFormatException>(() => ReadAllAsync(stream));

        error.LineNumber.ShouldBe(3);
        error.Message.ShouldContain("line 3");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task Reader_rejects_snapshots_with_missing_data(string snapshot)
    {
        using var stream = FromText("{\"format\":\"fonia-telemetry\",\"version\":1}\n" + snapshot + "\n");

        var error = await Should.ThrowAsync<TelemetryFormatException>(() => ReadAllAsync(stream));

        error.LineNumber.ShouldBe(2);
    }

    [Fact]
    public async Task Reader_yields_snapshots_before_a_later_error()
    {
        using var written = await WriteAsync(MockAircraft.Parked());
        var lines = Text(written).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var stream = FromText($"{lines[0]}\n{lines[1]}\n{{ broken\n");
        var received = new List<AircraftState>();

        await Should.ThrowAsync<TelemetryFormatException>(async () =>
        {
            await foreach (var state in TelemetryReader.ReadAsync(stream, Ct))
            {
                received.Add(state);
            }
        });

        received.ShouldBe([MockAircraft.Parked()]);
    }

    [Fact]
    public async Task Reader_leaves_the_stream_open()
    {
        using var stream = await WriteAsync(MockAircraft.Parked());

        await ReadAllAsync(stream);

        stream.CanRead.ShouldBeTrue();
    }
}
