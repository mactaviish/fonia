using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Tests.TestSupport;
using Fonia.Core.Weather;

namespace Fonia.Core.Tests.Atc;

// The pilot says which ATIS information was heard (ICA 100-37, Art. 759, 766 and 767).
public class AtisReceivedTests
{
    private const string Own = "PRFAB";

    private static readonly AirportContext Base = AirportContext.Create(TestAirports.WithSolo());

    private static readonly AirportContext OnSolo = FrequencyResolver.Apply(Base, Base.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency);

    private static readonly FlightContext Parked = new(Own, FlightPhase.Parked, "SBXX", null);

    private readonly RuleBasedNlu _nlu = new();
    private readonly AtcEngine _engine = new();
    private readonly PortuguesePhraseology _phraseology = new();

    // The report is from 12:30 UTC, so the current information is MIKE.
    private static AirportContext WithWeather(AirportContext airport)
    {
        MetarParser.TryParse("SBXX 151230Z 15008KT CAVOK 22/14 Q1018", out var metar).ShouldBeTrue();
        return airport.WithWeather(metar, keepRunway: false);
    }

    private string? Answer(string said, AirportContext airport)
    {
        var decision = _engine.Handle(_nlu.Interpret(said, Parked), Parked, airport);
        return _phraseology.Speak(decision.Instruction, new PhraseContext(Own, airport))?.Text;
    }

    [Theory]
    [InlineData("Solo, Papa Romeo Foxtrot Alfa Bravo, com informação Mike", 'M')]
    [InlineData("Papa Romeo Foxtrot Alfa Bravo informacao Mike", 'M')]
    [InlineData("Solo, Foxtrot Alfa Bravo, com atis Alfa", 'A')]
    [InlineData("Bom dia, Papa Romeo Foxtrot Alfa Bravo, recebi a informação Charlie", 'C')]
    public void Saying_the_information_heard_is_understood(string said, char letter)
    {
        _nlu.Interpret(said, Parked).ShouldBe(new ReportAtis(Own, letter));
    }

    [Fact]
    public void A_request_with_the_information_is_still_the_request()
    {
        _nlu.Interpret("Solo, Papa Romeo Foxtrot Alfa Bravo, solicito acionamento com informação Mike", Parked).ShouldBeOfType<RequestStartup>();
    }

    [Fact]
    public void Asking_for_the_information_is_not_saying_a_letter()
    {
        _nlu.Interpret("Solo, Papa Romeo Foxtrot Alfa Bravo, solicito informações de partida", Parked).ShouldBeOfType<RequestInformation>();
    }

    [Fact]
    public void With_the_current_information_the_answer_is_the_altimeter_setting()
    {
        Answer("Solo, Papa Romeo Foxtrot Alfa Bravo, com informação Mike", WithWeather(OnSolo))
            .ShouldBe("PAPA ROMEO FOXTROT ALFA BRAVO, ajuste de altímetro UNO ZERO UNO OITO.", StringCompareShould.IgnoreCase);
    }

    [Fact]
    public void With_an_outdated_information_the_current_one_is_told_with_the_altimeter_setting()
    {
        Answer("Solo, Papa Romeo Foxtrot Alfa Bravo, com informação Alfa", WithWeather(OnSolo))
            .ShouldBe("PAPA ROMEO FOXTROT ALFA BRAVO, informação MIKE em vigor, ajuste de altímetro UNO ZERO UNO OITO.", StringCompareShould.IgnoreCase);
    }

    [Fact]
    public void Without_weather_there_is_nothing_to_tell()
    {
        Answer("Solo, Papa Romeo Foxtrot Alfa Bravo, com informação Mike", OnSolo).ShouldBeNull();
    }

    [Fact]
    public void Without_a_unit_on_the_frequency_nobody_answers()
    {
        var silent = WithWeather(Base) with { Unit = ServiceUnit.None, Tuned = new Fonia.Core.Sim.Frequency(130000) };

        Answer("Solo, Papa Romeo Foxtrot Alfa Bravo, com informação Mike", silent)!.ShouldNotContain("ajuste");
    }
}
