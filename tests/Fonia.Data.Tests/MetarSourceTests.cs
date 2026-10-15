using System.Net;
using Fonia.Core.Weather;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.Data.Tests;

public sealed class MetarSourceTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Json = """[{"icaoId":"SBCT","obsTime":1760529600,"rawOb":"SBCT 151200Z 09008KT 9999 SCT030 22/14 Q1018"}]""";

    private static readonly DateTimeOffset Start = new(2026, 10, 15, 12, 10, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fonia-metar-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(reply(request));
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no network");
    }

    private sealed class FakeSource(Func<MetarReading?> reply) : IMetarSource
    {
        public int Calls { get; private set; }

        public Task<MetarReading?> GetAsync(string icao, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(reply());
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static AviationWeatherMetarSource Source(HttpMessageHandler handler) => new(new HttpClient(handler));

    private static MetarReading Reading(string raw, DateTimeOffset observed)
    {
        MetarParser.TryParse(raw, out var metar).ShouldBeTrue();
        return new MetarReading(metar, observed);
    }

    private CachedMetarSource Cached(IMetarSource inner, TimeProvider time) =>
        new(inner, _directory, time, TimeSpan.FromMinutes(30), TimeSpan.FromHours(3));

    [Fact]
    public async Task The_reply_of_the_service_becomes_a_metar_with_the_time_it_was_observed()
    {
        var handler = new FakeHandler(_ => Ok(Json));

        var reading = await Source(handler).GetAsync("sbct", Ct);

        reading.ShouldNotBeNull();
        reading.Metar.QnhHectopascals.ShouldBe(1018);
        reading.Metar.Wind!.SpeedKnots.ShouldBe(8);
        reading.ObservedAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1760529600));
        reading.Stale.ShouldBeFalse();
        handler.Requests.Single().ToString().ShouldBe("https://aviationweather.gov/api/data/metar?ids=SBCT&format=json");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("""[{"icaoId":"SBCT"}]""")]
    [InlineData("""[{"rawOb":"nothing useful"}]""")]
    [InlineData("""{"error":"x"}""")]
    [InlineData("[1]")]
    public async Task A_reply_without_a_usable_metar_is_no_metar(string body)
    {
        (await Source(new FakeHandler(_ => Ok(body))).GetAsync("SBCT", Ct)).ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_reply_that_is_not_ok_is_no_metar(HttpStatusCode status)
    {
        (await Source(new FakeHandler(_ => new HttpResponseMessage(status))).GetAsync("SBCT", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_network_that_fails_is_no_metar_and_not_an_exception()
    {
        (await Source(new FailingHandler()).GetAsync("SBCT", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Cancelling_is_still_cancelling()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Source(new FakeHandler(_ => Ok(Json))).GetAsync("SBCT", cancelled.Token));
    }

    [Fact]
    public async Task A_fresh_report_is_not_asked_for_again()
    {
        var time = new FakeTimeProvider(Start);
        var inner = new FakeSource(() => Reading("SBCT 151200Z 09008KT 9999 22/14 Q1018", Start.AddMinutes(-10)));
        var cached = Cached(inner, time);

        await cached.GetAsync("SBCT", Ct);
        time.Advance(TimeSpan.FromMinutes(20));
        var second = await cached.GetAsync("SBCT", Ct);

        inner.Calls.ShouldBe(1);
        second!.Metar.QnhHectopascals.ShouldBe(1018);
        second.Stale.ShouldBeFalse();
    }

    [Fact]
    public async Task An_old_report_is_asked_for_again_and_the_new_one_wins()
    {
        var time = new FakeTimeProvider(Start);
        var raw = "SBCT 151200Z 09008KT 9999 22/14 Q1018";
        var inner = new FakeSource(() => Reading(raw, Start.AddMinutes(-10)));
        var cached = Cached(inner, time);

        await cached.GetAsync("SBCT", Ct);
        time.Advance(TimeSpan.FromMinutes(40));
        raw = "SBCT 151250Z 12010KT 9999 22/14 Q1016";
        var second = await cached.GetAsync("SBCT", Ct);

        inner.Calls.ShouldBe(2);
        second!.Metar.QnhHectopascals.ShouldBe(1016);
    }

    [Fact]
    public async Task When_the_source_fails_the_last_report_is_used_and_marked_stale()
    {
        var time = new FakeTimeProvider(Start);
        var up = true;
        var inner = new FakeSource(() => up ? Reading("SBCT 151200Z 09008KT 9999 22/14 Q1018", Start.AddMinutes(-10)) : null);
        var cached = Cached(inner, time);

        await cached.GetAsync("SBCT", Ct);
        up = false;
        time.Advance(TimeSpan.FromHours(1));
        var second = await cached.GetAsync("SBCT", Ct);

        second.ShouldNotBeNull();
        second.Stale.ShouldBeTrue();
        second.Metar.QnhHectopascals.ShouldBe(1018);
    }

    [Fact]
    public async Task A_report_too_old_is_worse_than_none()
    {
        var time = new FakeTimeProvider(Start);
        var up = true;
        var inner = new FakeSource(() => up ? Reading("SBCT 151200Z 09008KT 9999 22/14 Q1018", Start.AddMinutes(-10)) : null);
        var cached = Cached(inner, time);

        await cached.GetAsync("SBCT", Ct);
        up = false;
        time.Advance(TimeSpan.FromHours(4));

        (await cached.GetAsync("SBCT", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task The_cache_survives_a_restart_and_each_airport_has_its_own()
    {
        var time = new FakeTimeProvider(Start);
        await Cached(new FakeSource(() => Reading("SBCT 151200Z 09008KT 9999 22/14 Q1018", Start.AddMinutes(-10))), time).GetAsync("SBCT", Ct);

        var offline = new FakeSource(() => null);
        var afterRestart = await Cached(offline, time).GetAsync("SBCT", Ct);
        var other = await Cached(offline, time).GetAsync("SBGR", Ct);

        afterRestart!.Metar.QnhHectopascals.ShouldBe(1018);
        other.ShouldBeNull();
    }

    [Fact]
    public async Task A_cache_that_is_garbage_or_cannot_be_written_does_not_stop_anything()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "SBCT.json"), "garbage", Ct);
        var time = new FakeTimeProvider(Start);
        var inner = new FakeSource(() => Reading("SBCT 151200Z 09008KT 9999 22/14 Q1018", Start.AddMinutes(-10)));

        (await Cached(inner, time).GetAsync("SBCT", Ct))!.Metar.QnhHectopascals.ShouldBe(1018);

        // A file where the folder should be: the cache cannot be written, the report still comes.
        var blocked = Path.Combine(Path.GetTempPath(), "fonia-metar-blocked-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(blocked, "x", Ct);

        try
        {
            var noCache = new CachedMetarSource(inner, blocked, time, TimeSpan.FromMinutes(30), TimeSpan.FromHours(3));

            (await noCache.GetAsync("SBCT", Ct))!.Metar.QnhHectopascals.ShouldBe(1018);
        }
        finally
        {
            File.Delete(blocked);
        }
    }
}
