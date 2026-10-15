using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// A readback shows the clearance was heard: the callsign and the action are what it needs. The pilot at the holding point of a runway does
// not repeat which runway it is, but a runway that is named and is wrong is still "negativo".
public class LenientReadbackTests
{
    private const string Own = "PRFRA";

    private static readonly AirportContext SoloAndTower = AirportContext.Create(TestAirports.WithSolo());

    private static readonly AirportContext OnTower =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private static readonly AirportContext OnSolo =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency);

    private readonly RuleBasedNlu _nlu = new();
    private readonly AtcEngine _engine = new();

    private AtcDecision Say(string said, AtcInstruction pending, FlightPhase phase, AirportContext airport)
    {
        var flight = new FlightContext(Own, phase, "SBCT", pending, pending, null, "15");

        return _engine.Handle(_nlu.Interpret(said, flight), flight, airport);
    }

    [Theory]
    [InlineData("Alinhe e mantenha, Papa Romeo Foxtrot Romeo Alfa")]
    [InlineData("Alinhando, Foxtrot Romeo Alfa. Alinhe e mantenha")]
    [InlineData("Alinhe e mantenha pista uno cinco, Foxtrot Romeo Alfa")]
    public void The_line_up_is_heard_with_or_without_the_runway(string said)
    {
        Say(said, new LineUpAndWait("15"), FlightPhase.LineUpCleared, OnTower).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Theory]
    [InlineData("Autorizado decolagem, Papa Romeo Foxtrot Romeo Alfa")]
    [InlineData("Decolagem autorizada, Foxtrot Romeo Alfa")]
    [InlineData("Autorizado decolagem pista uno cinco, Foxtrot Romeo Alfa")]
    public void The_takeoff_clearance_is_heard_with_or_without_the_runway(string said)
    {
        Say(said, new TakeoffClearance("15"), FlightPhase.TakeoffCleared, OnTower).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Theory]
    [InlineData("Pouso autorizado, Papa Romeo Foxtrot Romeo Alfa")]
    [InlineData("Autorizado pousar pista uno cinco, Foxtrot Romeo Alfa")]
    public void The_landing_clearance_is_heard_with_or_without_the_runway(string said)
    {
        Say(said, new LandingClearance("15"), FlightPhase.LandingCleared, OnTower).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Theory]
    [InlineData("Autorizado circuito de tráfego, Papa Romeo Foxtrot Romeo Alfa")]
    [InlineData("Circuito, reporto perna do vento, Foxtrot Romeo Alfa")]
    public void The_circuit_clearance_is_heard_with_or_without_the_runway(string said)
    {
        Say(said, new CircuitClearance("15"), FlightPhase.InCircuit, OnTower).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Theory]
    [InlineData("Autorizado táxi, Papa Romeo Foxtrot Romeo Alfa")]
    [InlineData("Taxiando para o ponto de espera, Foxtrot Romeo Alfa")]
    [InlineData("Autorizado táxi para o ponto de espera da pista uno cinco, Foxtrot Romeo Alfa")]
    public void The_taxi_clearance_is_heard_with_or_without_the_runway(string said)
    {
        Say(said, new TaxiClearance("15", 1014), FlightPhase.Taxiing, OnSolo).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void The_numbers_of_the_altimeter_setting_are_not_taken_for_a_runway()
    {
        Say("Autorizado táxi, ajuste de altímetro uno zero uno quatro, Foxtrot Romeo Alfa", new TaxiClearance("15", 1014), FlightPhase.Taxiing, OnSolo)
            .Instruction.ShouldBeOfType<NoResponse>();
    }

    [Theory]
    [InlineData("Alinhe e mantenha pista uno nove, Papa Romeo Foxtrot Romeo Alfa")]
    [InlineData("Alinhe e mantenha pista três três, Foxtrot Romeo Alfa")]
    public void A_runway_that_is_named_and_is_wrong_is_still_corrected(string said)
    {
        Say(said, new LineUpAndWait("15"), FlightPhase.LineUpCleared, OnTower).Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }

    [Theory]
    [InlineData("Autorizado decolagem pista uno nove, Foxtrot Romeo Alfa", "takeoff")]
    [InlineData("Pouso autorizado pista dois três, Foxtrot Romeo Alfa", "landing")]
    [InlineData("Autorizado táxi pista uno nove, Foxtrot Romeo Alfa", "taxi")]
    public void The_wrong_runway_is_corrected_in_every_clearance(string said, string kind)
    {
        AtcInstruction pending = kind switch
        {
            "takeoff" => new TakeoffClearance("15"),
            "landing" => new LandingClearance("15"),
            _ => new TaxiClearance("15"),
        };
        var phase = kind switch { "takeoff" => FlightPhase.TakeoffCleared, "landing" => FlightPhase.LandingCleared, _ => FlightPhase.Taxiing };

        Say(said, pending, phase, kind == "taxi" ? OnSolo : OnTower).Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }

    [Theory]
    [InlineData("Ciente, Papa Romeo Foxtrot Romeo Alfa")]
    [InlineData("Pista uno cinco, Foxtrot Romeo Alfa")]
    public void A_readback_with_no_action_of_the_clearance_is_corrected(string said)
    {
        Say(said, new LineUpAndWait("15"), FlightPhase.LineUpCleared, OnTower).Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }

    [Fact]
    public void The_callsign_is_still_needed_even_when_everything_else_is_right()
    {
        Say("Alinhe e mantenha pista uno cinco", new LineUpAndWait("15"), FlightPhase.LineUpCleared, OnTower)
            .Instruction.ShouldBeOfType<RequestReadbackCorrection>();
    }
}
