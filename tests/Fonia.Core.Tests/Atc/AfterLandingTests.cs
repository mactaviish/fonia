using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// After the landing: the Torre says the aircraft is on the ground and where to call (MCA 100-16/2025, Art. 133), and the pilot who has
// vacated the runway is taken to the apron (Art. 134).
public class AfterLandingTests
{
    private const string Own = "PRABC";

    private static readonly AirportContext SoloAndTower = AirportContext.Create(TestAirports.WithSolo());

    private static readonly Frequency SoloFrequency = SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency;

    private static readonly AirportContext OnTower = FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private static readonly AirportContext OnSolo = FrequencyResolver.Apply(SoloAndTower, SoloFrequency);

    private static readonly AirportContext OnlyTower =
        AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)], AirportFrequencyType.Tower));

    private static readonly FlightContext Cleared = new(Own, FlightPhase.LandingCleared, "SBXX", null, null, null, "11");

    private static readonly FlightContext Landed = Cleared with { Phase = FlightPhase.Landed };

    private static TouchedDown At(int minute) => new(new DateTimeOffset(2026, 10, 14, 12, minute, 31, TimeSpan.Zero));

    [Fact]
    public void The_torre_that_cleared_the_landing_says_the_minute_and_where_to_call_the_solo()
    {
        var decision = new AtcEngine().Handle(At(2), Cleared, OnTower);

        decision.Instruction.ShouldBe(new LandedNotice(2, SoloFrequency));
        decision.NextContext.Phase.ShouldBe(FlightPhase.Landed);
        decision.NextContext.LastInstruction.ShouldBe(new LandedNotice(2, SoloFrequency));
    }

    [Fact]
    public void A_torre_without_a_solo_only_says_the_aircraft_is_on_the_ground()
    {
        new AtcEngine().Handle(At(2), Cleared, FrequencyResolver.Apply(OnlyTower, OnlyTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency))
            .Instruction.ShouldBe(new LandedNotice(2, null));
    }

    [Theory]
    [InlineData(FlightPhase.Airborne)]
    [InlineData(FlightPhase.InCircuit)]
    [InlineData(FlightPhase.OnBase)]
    [InlineData(FlightPhase.Landed)]
    [InlineData(FlightPhase.Taxiing)]
    public void A_landing_nobody_cleared_is_none_of_the_controllers_business(FlightPhase phase)
    {
        var flight = Cleared with { Phase = phase };

        var decision = new AtcEngine().Handle(At(2), flight, OnTower);

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.ShouldBe(flight);
    }

    [Fact]
    public void A_pilot_who_already_left_the_torres_frequency_hears_nothing()
    {
        new AtcEngine().Handle(At(2), Cleared, OnSolo).Instruction.ShouldBeOfType<NoResponse>();
    }

    // The pilot acknowledges what the Torre said: an answer, not something to ask "repita" about.
    [Fact]
    public void The_pilots_acknowledgement_of_the_notices_is_taken_in_silence()
    {
        var engine = new AtcEngine();
        var airborne = engine.Handle(new TookOff(At(2).At), Cleared with { Phase = FlightPhase.TakeoffCleared }, OnTower).NextContext;
        var landed = engine.Handle(At(2), Cleared, OnTower).NextContext;

        engine.Handle(new Readback(Own, "ciente chamando solo"), airborne, OnTower).Instruction.ShouldBeOfType<NoResponse>();
        engine.Handle(new Readback(Own, "ciente chamando solo"), landed, OnTower).Instruction.ShouldBeOfType<NoResponse>();
        engine.Handle(new Readback(Own, "ciente chamando solo"), landed, OnTower).NextContext.PendingReadback.ShouldBeNull();
    }

    [Fact]
    public void The_pilot_who_vacated_the_runway_on_the_torre_is_told_to_call_the_solo()
    {
        var decision = new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed, OnTower);

        decision.Instruction.ShouldBe(new HandoffToGround(SoloFrequency));
        decision.NextContext.PendingReadback.ShouldBe(new HandoffToGround(SoloFrequency));
    }

    [Fact]
    public void The_solo_takes_the_pilot_who_vacated_the_runway_to_the_apron_and_the_flight_can_start_over()
    {
        var decision = new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed, OnSolo);

        decision.Instruction.ShouldBeOfType<TaxiToApron>();
        decision.NextContext.Phase.ShouldBe(FlightPhase.Parked);
        decision.NextContext.PendingReadback.ShouldBeOfType<TaxiToApron>();
        decision.NextContext.AssignedRunway.ShouldBeNull();
    }

    [Fact]
    public void A_torre_alone_takes_the_pilot_to_the_apron_itself()
    {
        var onlyTower = FrequencyResolver.Apply(OnlyTower, OnlyTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

        new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed, onlyTower).Instruction.ShouldBeOfType<TaxiToApron>();
    }

    [Theory]
    [InlineData(FlightPhase.Parked)]
    [InlineData(FlightPhase.Taxiing)]
    [InlineData(FlightPhase.TakeoffCleared)]
    public void Vacating_a_runway_that_was_never_landed_on_makes_no_sense(FlightPhase phase)
    {
        new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed with { Phase = phase }, OnSolo).Instruction.ShouldBeOfType<SayAgainRequest>();
    }

    [Theory]
    [InlineData("taxi para o pátio, Papa Romeo Alfa Bravo Charlie", true)]
    [InlineData("taxiando para o pátio, Papa Romeo Alfa Bravo Charlie", true)]
    [InlineData("ciente, Papa Romeo Alfa Bravo Charlie", false)]
    public void The_taxi_to_the_apron_is_read_back_with_the_word_patio_and_the_callsign(string said, bool correct)
    {
        var told = new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed, OnSolo).NextContext;

        var decision = new AtcEngine().Handle(new Readback(Own, string.Join(' ', SpokenTokens.Tokenize(said))), told, OnSolo);

        (decision.Instruction is NoResponse).ShouldBe(correct);
    }

    [Fact]
    public void The_notices_are_phrased_from_the_manual()
    {
        var phraseology = new PortuguesePhraseology();
        var context = new PhraseContext(Own, OnTower);

        phraseology.Speak(new LandedNotice(2, SoloFrequency), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, no solo aos ZERO DOIS, após livrar a pista chame Solo Curitiba UNO UNO OITO DECIMAL ZERO.");
        phraseology.Speak(new LandedNotice(2, null), context)!.Text.ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, no solo aos ZERO DOIS.");
        phraseology.Speak(new HandoffToGround(SoloFrequency), context)!.Text.ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, chame Solo Curitiba UNO UNO OITO DECIMAL ZERO.");
        phraseology.Speak(new TaxiToApron(), context)!.Text.ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, taxie para o pátio.");
    }
}
