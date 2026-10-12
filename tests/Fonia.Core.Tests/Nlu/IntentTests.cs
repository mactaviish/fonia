using Fonia.Core.Nlu;

namespace Fonia.Core.Tests.Nlu;

public class IntentTests
{
    [Fact]
    public void Intents_are_equal_by_value()
    {
        new RequestTaxi("PRABC", "15").ShouldBe(new RequestTaxi("PRABC", "15"));
        new RequestTaxi("PRABC", "15").ShouldNotBe(new RequestTaxi("PRABC", "33"));
    }

    [Fact]
    public void Different_intent_types_are_never_equal()
    {
        Intent taxi = new RequestTaxi("PRABC", null);
        Intent again = new SayAgain("PRABC");

        taxi.ShouldNotBe(again);
        taxi.Callsign.ShouldBe(again.Callsign);
    }

    [Fact]
    public void Unknown_keeps_raw_text_and_has_no_callsign()
    {
        Intent intent = new Unknown("blá blá");

        intent.Callsign.ShouldBeNull();
        intent.ShouldBeOfType<Unknown>().RawText.ShouldBe("blá blá");
    }
}
