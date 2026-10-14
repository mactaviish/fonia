using Fonia.Core.Speech;

namespace Fonia.Core.Tests.Speech;

public class VoiceSelectorTests
{
    private static readonly Voice Dora = new("pf_dora", "Dora", VoiceGender.Feminine);
    private static readonly Voice Alex = new("pm_alex", "Alex", VoiceGender.Masculine);
    private static readonly Voice Bia = new("pf_bia", "Bia", VoiceGender.Feminine);

    [Fact]
    public void The_catalog_offers_the_approved_voices_and_not_the_rejected_one()
    {
        VoiceCatalog.Default.Select(v => v.Id).ShouldBe(["pf_dora", "pm_alex"]);
        VoiceCatalog.Find("pm_santa").ShouldBeNull();
        VoiceCatalog.Find("PF_DORA").ShouldBe(Dora);
        VoiceCatalog.Find(null).ShouldBeNull();
    }

    [Fact]
    public void A_fixed_voice_is_used_for_every_station()
    {
        var selector = new VoiceSelector([Dora, Alex], "pm_alex", new Random(1));

        selector.For("SOLO SBCT").ShouldBe(Alex);
        selector.For("TORRE SBCT").ShouldBe(Alex);
    }

    [Fact]
    public void A_fixed_voice_that_is_not_in_the_pool_is_refused_with_the_list_of_those_that_are()
    {
        var error = Should.Throw<ArgumentException>(() => new VoiceSelector([Dora, Alex], "pm_santa"));

        error.Message.ShouldContain("pm_santa");
        error.Message.ShouldContain("pf_dora");
        error.Message.ShouldContain("pm_alex");
    }

    [Fact]
    public void A_station_keeps_its_voice_for_the_whole_session()
    {
        var selector = new VoiceSelector([Dora, Alex, Bia], null, new Random(42));

        var first = selector.For("SOLO SBCT");

        for (var i = 0; i < 20; i++)
        {
            selector.For("SOLO SBCT").ShouldBe(first);
        }
    }

    [Fact]
    public void Different_stations_get_different_voices_while_there_are_voices_left()
    {
        for (var seed = 0; seed < 30; seed++)
        {
            var selector = new VoiceSelector([Dora, Alex, Bia], null, new Random(seed));

            var voices = new[] { selector.For("SOLO"), selector.For("TORRE"), selector.For("RÁDIO") };

            voices.Distinct().Count().ShouldBe(3, $"seed {seed}");
        }
    }

    [Fact]
    public void With_more_stations_than_voices_voices_are_shared_but_every_station_still_gets_one()
    {
        var selector = new VoiceSelector([Dora, Alex], null, new Random(7));

        var voices = new[] { selector.For("A"), selector.For("B"), selector.For("C") };

        voices.ShouldAllBe(v => v == Dora || v == Alex);
        voices.Distinct().Count().ShouldBe(2, "the first two stations use the two voices");
    }

    [Fact]
    public void The_choice_is_random_between_sessions()
    {
        var chosen = Enumerable.Range(0, 40).Select(seed => new VoiceSelector([Dora, Alex], null, new Random(seed)).For("SOLO").Id).Distinct();

        chosen.Count().ShouldBe(2);
    }

    [Fact]
    public void An_empty_pool_is_refused()
    {
        Should.Throw<ArgumentException>(() => new VoiceSelector([], null));
    }

    [Fact]
    public async Task It_is_safe_to_ask_from_several_threads_at_once()
    {
        var selector = new VoiceSelector([Dora, Alex, Bia], null, new Random(3));

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => selector.For("SOLO"), TestContext.Current.CancellationToken)));

        results.Distinct().Count().ShouldBe(1);
    }
}
