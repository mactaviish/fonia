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
        decision.NextContext.PendingReadback.ShouldBeNull();
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

    [Fact]
    public void Asking_to_say_it_again_after_the_notice_repeats_it()
    {
        var engine = new AtcEngine();
        var flight = engine.Handle(At(26), Cleared, OnTower).NextContext;

        engine.Handle(new SayAgain(Own), flight, OnTower).Instruction.ShouldBe(new AirborneNotice(26));
    }
}
