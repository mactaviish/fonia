using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// The Torre tells the aircraft it is airborne (MCA 100-16/2025, Art. 127), driven by the telemetry and not by the pilot.
public class AirborneNoticeTests
{
    private const string Own = "PRABC";

    private static readonly AirportContext SoloAndTower = AirportContext.Create(TestAirports.WithSolo());

    private static readonly AirportContext OnTower =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private static readonly AirportContext OnSolo =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency);

    private static readonly FlightContext Cleared = new(Own, FlightPhase.TakeoffCleared, "SBXX", null, null, null, "11");

    private static TookOff At(int minute) => new(new DateTimeOffset(2026, 10, 14, 12, minute, 31, TimeSpan.Zero));

    [Fact]
    public void The_torre_that_cleared_the_takeoff_says_the_minute_the_aircraft_left_the_ground()
    {
        var decision = new AtcEngine().Handle(At(26), Cleared, OnTower);

        decision.Instruction.ShouldBe(new AirborneNotice(26));
        decision.NextContext.Phase.ShouldBe(FlightPhase.Airborne);
        decision.NextContext.PendingReadback.ShouldBe(new AirborneNotice(26));
        decision.NextContext.LastInstruction.ShouldBe(new AirborneNotice(26));
    }

    [Fact]
    public void The_minute_is_in_utc_whatever_the_offset_of_the_snapshot()
    {
        var local = new TookOff(new DateTimeOffset(2026, 10, 14, 9, 26, 0, TimeSpan.FromHours(-3)));

        new AtcEngine().Handle(local, Cleared, OnTower).Instruction.ShouldBe(new AirborneNotice(26));
    }

    [Theory]
    [InlineData(FlightPhase.Parked)]
    [InlineData(FlightPhase.Taxiing)]
    [InlineData(FlightPhase.HandedToTower)]
    [InlineData(FlightPhase.LineUpCleared)]
    [InlineData(FlightPhase.Airborne)]
    public void A_takeoff_nobody_cleared_is_none_of_the_controllers_business(FlightPhase phase)
    {
        var flight = Cleared with { Phase = phase };

        var decision = new AtcEngine().Handle(At(26), flight, OnTower);

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.ShouldBe(flight);
    }

    [Fact]
    public void A_pilot_who_already_left_the_torres_frequency_hears_nothing()
    {
        var decision = new AtcEngine().Handle(At(26), Cleared, OnSolo);

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.ShouldBe(Cleared);
    }

    [Fact]
    public void After_taking_off_a_request_for_departure_is_not_cleared_again()
    {
        var airborne = Cleared with { Phase = FlightPhase.Airborne };

        new AtcEngine().Handle(new RequestDeparture(Own, "11"), airborne, OnTower).Instruction.ShouldBeOfType<SayAgainRequest>();
    }

    [Fact]
    public void The_notice_is_the_callsign_and_the_minutes_digit_by_digit()
    {
        var phraseology = new PortuguesePhraseology();
        var context = new PhraseContext(Own, OnTower);

        phraseology.Speak(new AirborneNotice(26), context)!.Text.ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, decolado aos DOIS MEIA.");
        phraseology.Speak(new AirborneNotice(2), context)!.Text.ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, decolado aos ZERO DOIS.");
    }

    private static readonly AirportContext WithApproach = AirportContext.Create(TestAirports.Airport(
        "SBXX",
        [TestAirports.Runway("11", "29", 9000)],
        AirportFrequencyType.Ground,
        AirportFrequencyType.Tower,
        AirportFrequencyType.Approach));

    private static readonly AirportContext OnTowerWithApproach =
        FrequencyResolver.Apply(WithApproach, WithApproach.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private static Frequency Approach => WithApproach.Airport.FrequencyOf(AirportFrequencyType.Approach)!.Frequency;

    // Art. 127, IV: the next unit and its frequency. It is the Controle of the airport, when the data has one.
    [Fact]
    public void An_airport_with_an_approach_passes_the_flight_to_the_controle_with_its_frequency()
    {
        var decision = new AtcEngine().Handle(At(26), Cleared, OnTowerWithApproach);

        decision.Instruction.ShouldBe(new AirborneNotice(26, Approach));
        decision.NextContext.PendingReadback.ShouldBe(new AirborneNotice(26, Approach));
        decision.NextContext.Phase.ShouldBe(FlightPhase.Airborne);
    }

    // The Controle of a big airport has many sectors and the simulator does not say which is the departure: with none named, none is said.
    [Fact]
    public void An_approach_with_many_sectors_and_none_named_says_only_the_minute()
    {
        var sectors = Enumerable.Range(0, 3).Select(i => new AirportFrequency(AirportFrequencyType.Approach, new Frequency(119600 + (i * 150)), "CONTROLE")).ToArray();
        var merged = AirportFrequencyMerge.Apply(WithApproach.Airport, sectors);
        var onTower = FrequencyResolver.Apply(AirportContext.Create(merged), WithApproach.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

        new AtcEngine().Handle(At(26), Cleared, onTower).Instruction.ShouldBe(new AirborneNotice(26, null));
        FrequencyResolver.UnitOn(merged, new Frequency(119750)).ShouldBe(ServiceUnit.Approach);
    }

    [Fact]
    public void An_airport_without_an_approach_says_only_the_minute_as_before()
    {
        new AtcEngine().Handle(At(26), Cleared, OnTower).Instruction.ShouldBe(new AirborneNotice(26, null));
    }

    [Fact]
    public void The_notice_with_the_controle_is_the_minute_and_then_where_to_call()
    {
        var phraseology = new PortuguesePhraseology();
        var context = new PhraseContext(Own, OnTowerWithApproach);

        phraseology.Speak(new AirborneNotice(26, Approach), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, decolado aos DOIS MEIA, chame Controle Curitiba UNO UNO OITO DECIMAL DOIS.");
    }

    [Theory]
    [InlineData("Controle Curitiba uno uno oito decimal dois, Papa Romeo Alfa Bravo Charlie")]
    [InlineData("chamando Controle uno uno oito decimal dois")]
    [InlineData("ciente, Alfa Bravo Charlie")]
    public void What_the_pilot_says_after_the_controle_handoff_is_taken_in_silence(string said)
    {
        var flight = new AtcEngine().Handle(At(26), Cleared, OnTowerWithApproach).NextContext;

        var decision = new AtcEngine().Handle(new RuleBasedNlu().Interpret(said, flight), flight, OnTowerWithApproach);

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Fact]
    public void Asking_to_say_it_again_after_the_controle_handoff_repeats_it()
    {
        var engine = new AtcEngine();
        var flight = engine.Handle(At(26), Cleared, OnTowerWithApproach).NextContext;

        engine.Handle(new SayAgain(Own), flight, OnTowerWithApproach).Instruction.ShouldBe(new AirborneNotice(26, Approach));
    }

    [Fact]
    public void Asking_to_say_it_again_after_the_notice_repeats_it()
    {
        var engine = new AtcEngine();
        var flight = engine.Handle(At(26), Cleared, OnTower).NextContext;

        engine.Handle(new SayAgain(Own), flight, OnTower).Instruction.ShouldBe(new AirborneNotice(26));
    }
}
