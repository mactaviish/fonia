using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// With the map of the airport and the position of the aircraft, the Solo says the taxiways of the route (MCA 100-16/2025, Art. 125, II:
// "...para o ponto de espera da pista 10, via taxiway "G""). A route that crosses a runway still gets the plain clearance of today: the
// crossing is the next step. Without a map, a position or a safe route nothing changes.
public class TaxiRouteClearanceTests
{
    private const string Own = "PRABC";

    private static readonly AirportLayout Toy = TestLayouts.Toy();

    private readonly AtcEngine _engine = new();

    private static AirportContext Airport(string runway, AirportLayout? layout) =>
        AirportContext.Create(TestAirports.WithSolo(), runway) with { Layout = layout };

    private static AircraftState AtNode(int id, bool onGround = true, double latitudeShift = 0)
    {
        var position = Toy.Nodes.Single(n => n.Id == id).Position;

        return new AircraftState(
            DateTimeOffset.UtcNow, position.LatitudeDegrees + latitudeShift, position.LongitudeDegrees, 3000, onGround ? 0 : 500, 0, 110, onGround, null, null, 1200, "PR-ABC", "C172");
    }

    private AtcDecision Ask(AirportContext airport, AircraftState? aircraft)
    {
        var flight = new FlightContext(Own, FlightPhase.Parked, "SBXX", null, Aircraft: aircraft);

        return _engine.Handle(new RequestTaxi(Own, null), flight, airport);
    }

    [Fact]
    public void With_the_map_and_the_position_the_clearance_names_the_taxiways_of_the_route()
    {
        var decision = Ask(Airport("11", Toy), AtNode(1));

        decision.Instruction.ShouldBe(new TaxiClearance("11", null, ["A"]));
        decision.NextContext.PendingReadback.ShouldBe(new TaxiClearance("11", null, ["A"]));
        decision.NextContext.Phase.ShouldBe(FlightPhase.Taxiing);
        decision.NextContext.AssignedRunway.ShouldBe("11");
    }

    [Fact]
    public void The_altimeter_setting_is_still_there_when_there_is_one()
    {
        var airport = Airport("11", Toy) with { Qnh = 1013 };

        Ask(airport, AtNode(1)).Instruction.ShouldBe(new TaxiClearance("11", 1013, ["A"]));
    }

    [Fact]
    public void A_route_that_crosses_a_runway_gets_the_plain_clearance_for_now()
    {
        // Runway 15 is behind runway 11/29: the crossing needs its own steps, and a route over a runway is not said half way.
        Ask(Airport("15", Toy), AtNode(1)).Instruction.ShouldBe(new TaxiClearance("15"));
    }

    [Fact]
    public void Without_a_map_the_clearance_is_the_one_of_today()
    {
        Ask(Airport("11", null), AtNode(1)).Instruction.ShouldBe(new TaxiClearance("11"));
    }

    [Fact]
    public void Without_the_position_of_the_aircraft_the_clearance_is_the_one_of_today()
    {
        Ask(Airport("11", Toy), null).Instruction.ShouldBe(new TaxiClearance("11"));
    }

    [Fact]
    public void An_aircraft_that_is_not_on_the_ground_gets_the_plain_clearance()
    {
        Ask(Airport("11", Toy), AtNode(1, onGround: false)).Instruction.ShouldBe(new TaxiClearance("11"));
    }

    [Fact]
    public void An_aircraft_far_from_the_graph_gets_the_plain_clearance()
    {
        Ask(Airport("11", Toy), AtNode(1, latitudeShift: 0.05)).Instruction.ShouldBe(new TaxiClearance("11"));
    }

    [Fact]
    public void A_scenery_without_taxiways_gets_the_plain_clearance()
    {
        Ask(Airport("11", TestLayouts.RunwaysOnly()), AtNode(1)).Instruction.ShouldBe(new TaxiClearance("11"));
    }

    [Fact]
    public void Two_clearances_are_the_same_when_they_name_the_same_taxiways()
    {
        new TaxiClearance("11", null, ["A", "B"]).ShouldBe(new TaxiClearance("11", null, ["A", "B"]));
        new TaxiClearance("11", null, ["A"]).ShouldNotBe(new TaxiClearance("11", null, ["B"]));
        new TaxiClearance("11", null, ["A"]).ShouldNotBe(new TaxiClearance("11"));
        new TaxiClearance("11", null, []).ShouldBe(new TaxiClearance("11"));
    }

    // Art. 125, II: the runway, then "via taxiway ...". Letters in the phonetic alphabet, as for the callsign; several in the order of the route.
    // TODO(fraseologia): the manual shows one taxiway ("via taxiway "G""); the way several are listed is the maintainer's to confirm.
    [Fact]
    public void The_taxiways_are_said_after_the_runway_in_the_phonetic_alphabet()
    {
        var phraseology = new PortuguesePhraseology();
        var context = new PhraseContext(Own, Airport("11", Toy));

        phraseology.Speak(new TaxiClearance("11", null, ["A"]), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, autorizado táxi para o ponto de espera da PISTA UNO UNO, via taxiway ALFA.");
        phraseology.Speak(new TaxiClearance("11", null, ["A", "B2"]), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, autorizado táxi para o ponto de espera da PISTA UNO UNO, via taxiway ALFA, BRAVO DOIS.");
        phraseology.Speak(new TaxiClearance("11", 1013, ["A"]), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, autorizado táxi para o ponto de espera da PISTA UNO UNO, via taxiway ALFA, ajuste de altímetro UNO ZERO UNO TRÊS.");
    }

    [Fact]
    public void Without_taxiways_the_phrase_is_the_one_of_today()
    {
        var phraseology = new PortuguesePhraseology();

        phraseology.Speak(new TaxiClearance("11"), new PhraseContext(Own, Airport("11", Toy)))!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, autorizado táxi para o ponto de espera da PISTA UNO UNO.");
    }

    private AtcDecision Read(string said, TaxiClearance pending)
    {
        var flight = new FlightContext(Own, FlightPhase.Taxiing, "SBXX", pending, pending, null, pending.Runway);

        return _engine.Handle(new Readback(Own, string.Join(' ', SpokenTokens.Tokenize(said))), flight, Airport(pending.Runway, Toy));
    }

    [Theory]
    [InlineData("autorizado táxi pista uno um via taxiway alfa")]
    [InlineData("autorizado táxi pista uno um")]
    [InlineData("táxi para o ponto de espera pista uno um via alfa")]
    public void The_readback_needs_the_runway_and_not_the_taxiways(string said)
    {
        var decision = Read(said, new TaxiClearance("11", null, ["A"]));

        decision.Instruction.ShouldBe(new NoResponse());
    }

    // The digits of a taxiway name ("B2") are not a runway.
    [Theory]
    [InlineData("autorizado táxi via taxiway bravo dois")]
    [InlineData("autorizado táxi ponto de espera via taxiway bravo dois")]
    [InlineData("autorizado táxi pista uno um via taxiway bravo dois")]
    public void The_digits_of_a_taxiway_name_are_not_taken_for_a_runway(string said)
    {
        Read(said, new TaxiClearance("11", null, ["B2"])).Instruction.ShouldBe(new NoResponse());
    }

    [Fact]
    public void A_readback_with_the_wrong_runway_is_still_corrected_with_the_taxiways_repeated()
    {
        var pending = new TaxiClearance("11", null, ["A"]);

        Read("autorizado táxi pista uno cinco via taxiway alfa", pending).Instruction.ShouldBe(new RequestReadbackCorrection(pending));
    }
}
