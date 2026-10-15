using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Pipeline;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.App.Tests;

public sealed class CaseRecordingObserverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fonia-observer-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class Inner : IVoiceLoopObserver
    {
        public List<string> Calls { get; } = [];

        public void OnHeard(string text) => Calls.Add("heard");

        public void OnReplied(ExchangeResult result) => Calls.Add("replied");

        public void OnAnnounced(AnnouncementResult result) => Calls.Add("announced");

        public void OnChannelBusy() => Calls.Add("busy");

        public void OnTooShort(TimeSpan held) => Calls.Add("short");

        public void OnFailed(Exception exception) => Calls.Add("failed");
    }

    private static readonly Airport Curitiba = new(
        "SBCT", "Curitiba", "Curitiba", AirportType.Large, 0, 0, null, "SBCT", null, "Curitiba", SpokenNameSource.Reference, true, [], []);

    private static ExchangeResult Result(Intent intent, AtcInstruction instruction) =>
        new("x", intent, new AtcDecision(instruction, new FlightContext("PRABC", FlightPhase.Parked, "SBCT", null)), null, null,
            new StageTimings(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero), "SOLO Curitiba", new FlightContext("PRABC", FlightPhase.Parked, "SBCT", null));

    [Fact]
    public void Everything_goes_on_to_the_screen_and_a_case_is_kept_and_told()
    {
        var inner = new Inner();
        var output = new StringWriter();
        var log = new CaseLog(_root, new FakeTimeProvider(DateTimeOffset.UtcNow), new PortuguesePhraseology(), AirportContext.Create(Curitiba), "PRABC", saveAudio: false);
        var observer = new CaseRecordingObserver(inner, log, output);

        observer.OnHeard("x");
        observer.OnReplied(Result(new Unknown("blá"), new SayAgainRequest()));
        observer.OnReplied(Result(new RequestStartup("PRABC", null), new StartupApproved()));
        observer.OnChannelBusy();
        observer.OnTooShort(TimeSpan.Zero);
        observer.OnFailed(new InvalidOperationException());

        inner.Calls.ShouldBe(["heard", "replied", "replied", "busy", "short", "failed"]);
        log.Cases.ShouldBe(1);
        output.ToString().ShouldContain("guardado para melhoria: nao-entendido");
        output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(1, "the exchange that went well is not mentioned");
    }
}
