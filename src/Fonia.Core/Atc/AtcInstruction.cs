using Fonia.Core.Sim;

namespace Fonia.Core.Atc;

/// <summary>
/// What the controller decided to instruct, as data. Spoken text is produced later
/// from phraseology templates, never here.
/// </summary>
public abstract record AtcInstruction;

/// <summary>
/// Taxi to the holding point of <paramref name="Runway"/>, with the altimeter setting when known (Art. 125). <paramref name="Taxiways"/> are the
/// taxiways of the route, in order ("via taxiway A, B"), when the airport map gave a route that is safe to say; null or empty otherwise.
/// </summary>
// TODO(fraseologia): the manual shows one taxiway ("via taxiway "G""); how several are listed is to be confirmed with the maintainer.
public sealed record TaxiClearance(string Runway, int? Qnh = null, IReadOnlyList<string>? Taxiways = null) : AtcInstruction
{
    // The taxiways compare by what they say, not by the list: two clearances over the same route are the same clearance.
    public bool Equals(TaxiClearance? other) =>
        other is not null && Runway == other.Runway && Qnh == other.Qnh && (Taxiways ?? []).SequenceEqual(other.Taxiways ?? []);

    public override int GetHashCode() => HashCode.Combine(Runway, Qnh, Taxiways?.Count ?? 0);
}

/// <summary>The pilot's readback did not match; ask again for <paramref name="Expected"/>.</summary>
public sealed record RequestReadbackCorrection(AtcInstruction Expected) : AtcInstruction;

/// <summary>Engine start approved ("acionamento aprovado", MCA 100-16 Art. 123).</summary>
public sealed record StartupApproved : AtcInstruction;

/// <summary>Pushback approved; <paramref name="WithStartup"/> when the engine start was approved in the same call.</summary>
public sealed record PushbackApproved(bool WithStartup) : AtcInstruction;

/// <summary>This airport has no control service for the request: <see cref="ServiceUnit.Afis"/> only informs, <see cref="ServiceUnit.None"/> has nobody.</summary>
public sealed record NoControlService(ServiceUnit Unit) : AtcInstruction;

/// <summary>The airport data has no usable runway, so there is none to taxi to.</summary>
public sealed record NoRunwayAvailable : AtcInstruction;

/// <summary>The request came without the aircraft's callsign, or with another one: the controller asks for it again.</summary>
// TODO(fraseologia): the manual has no phrase for asking the callsign again; the template has a proposal to confirm.
public sealed record RequestCallsign : AtcInstruction;

/// <summary>The pilot called the controller and nothing more: the controller invites them to go ahead.</summary>
// The phrase is "<callsign>, prossiga" (from the maintainer; the manual has none for the Solo).
public sealed record GoAhead : AtcInstruction;

/// <summary>The controller did not understand: "REPITA".</summary>
public sealed record SayAgainRequest : AtcInstruction;

/// <summary>
/// The Solo passes the flight to the Torre at the holding point: "chame Torre ..." with the Torre's frequency
/// (MCA 100-16, Art. 125, II; the name of the Torre is the practice of VATSIM Brasil).
/// </summary>
// TODO(fraseologia): the manual does not list the frequency change among what must be read back (Art. 12); no readback is asked.
public sealed record HandoffToTower(Frequency Frequency) : AtcInstruction;

/// <summary>The airport has no Torre to pass the flight to. A message of the app, not radio.</summary>
public sealed record NoTowerAvailable : AtcInstruction;

/// <summary>The radio is tuned to a frequency of no unit of this airport: nobody answers. A message of the app, not radio.</summary>
public sealed record NoOneOnFrequency(Frequency Frequency) : AtcInstruction;

/// <summary>
/// The pilot asked the Torre for something the Solo does (engine start, pushback, taxi) at an airport that has a Solo. A message
/// of the app, not radio: the manual has no phrase for it.
/// </summary>
public sealed record CallTheSolo(Frequency Frequency) : AtcInstruction;

/// <summary>"Alinhe e mantenha": enter the runway and wait there for the takeoff clearance (MCA 100-16, Art. 126, II).</summary>
// TODO(fraseologia): the manual shows "alinhe e mantenha" only inside examples (Art. 126, negative; Art. 49, conditional), never as a
// plain instruction; the runway first follows the order of Art. 126, II. To be confirmed with the maintainer.
public sealed record LineUpAndWait(string Runway) : AtcInstruction;

/// <summary>Takeoff clearance: the runway, the clearance and, when the wind is known, the wind (Art. 126, II).</summary>
// TODO(fraseologia): said without the wind while the app has no source of it.
public sealed record TakeoffClearance(string Runway, Wind? Wind = null) : AtcInstruction;

/// <summary>
/// Cleared to the traffic circuit on <paramref name="Runway"/>, with the wind when known, to report the downwind (MCA 100-16, Art. 129).
/// </summary>
// The altimeter setting is said when there is a METAR. The other form, the straight-in approach, is <see cref="StraightInClearance"/>.
public sealed record CircuitClearance(string Runway, Wind? Wind = null, int? Qnh = null) : AtcInstruction;

/// <summary>
/// Cleared for the straight-in approach to <paramref name="Runway"/>, with the wind and the altimeter setting when known, to report final
/// (MCA 100-16, Art. 129: "autorizada aproximação direta, pista 11, vento 150 graus, 15 kt, QNH 1013. Reporte final").
/// </summary>
public sealed record StraightInClearance(string Runway, Wind? Wind = null, int? Qnh = null) : AtcInstruction;

/// <summary>"Reporte perna base" (Art. 130, II). Art. 12 does not list reports among what must be read back.</summary>
public sealed record ReportBase : AtcInstruction;

/// <summary>"Reporte na final" (Art. 131, II).</summary>
public sealed record ReportOnFinal : AtcInstruction;

/// <summary>Landing clearance: the runway, the clearance and, when the wind is known, the wind (Art. 132, II).</summary>
public sealed record LandingClearance(string Runway, Wind? Wind = null) : AtcInstruction;

/// <summary>The pilot reported a go-around and the Torre takes note of it.</summary>
// TODO(fraseologia): the manual shows the go-around only as the handoff of Art. 136 ("Ciente, PT CTA, chame Controle ..."), which needs a
// next unit the airport data does not have; "ciente" alone is a proposal to confirm.
public sealed record GoAroundNoted : AtcInstruction;

/// <summary>The pilot asked the Solo for an arrival, which is the Torre's. A message of the app, not radio: the manual has no phrase for it.</summary>
public sealed record CallTheTower(Frequency Frequency) : AtcInstruction;

/// <summary>
/// The answer to "solicita informações de partida" (MCA 100-16, Art. 122): the runway in use, the wind, the altimeter setting, the
/// temperature, the visibility and the time check. What the airport does not know is null and is not said; the time comes from the clock
/// of the phraseology, not from the decision.
/// </summary>
// TODO(fraseologia): 9999 in a METAR is "10 km or more"; "visibilidade dez quilômetros ou mais" is a proposal, the manual shows only meters.
public sealed record DepartureInformation(string? Runway, Wind? Wind, int? Qnh, int? TemperatureCelsius, int? VisibilityMeters) : AtcInstruction;

/// <summary>
/// The answer to the pilot saying which ATIS information was heard: the altimeter setting, and the current information when the pilot's
/// is not it. <paramref name="Current"/> is null when the app has no information to compare with.
/// </summary>
// TODO(fraseologia): ICA 100-37 (Art. 759 and 767) says the unit informs the QNH and corrects an outdated information, but gives no phrase.
public sealed record AtisReceived(char? Current, char Said, int? Qnh) : AtcInstruction;

/// <summary>
/// "No solo aos 02, após livrar a pista chame Solo ...": the Torre tells the aircraft it is on the ground, with the minute it touched down and
/// the Solo's frequency (MCA 100-16, Art. 133). <paramref name="Solo"/> is null when the airport has no Solo, and then nothing more is said.
/// </summary>
// TODO(fraseologia): without a Solo the manual's "taxie via ..., reporte pista livre" needs a taxiway the airport data does not have.
public sealed record LandedNotice(int Minute, Frequency? Solo) : AtcInstruction;

/// <summary>The Torre passes a flight that cleared the runway to the Solo: "chame Solo ..." (Art. 133). Like the handoff to the Torre, no readback is asked.</summary>
public sealed record HandoffToGround(Frequency Frequency) : AtcInstruction;

/// <summary>
/// "Cruze a pista 10L, após chame Solo ...": the Torre clears the aircraft that vacated the landing runway to cross another one on its way to
/// the apron, and says where to call after the crossing. <paramref name="Solo"/> is null when the airport has no Solo, and then nothing more is said.
/// </summary>
// TODO(fraseologia): the phrase is the practice the maintainer gave (an arrival that vacated 10R and is told to cross 10L), not the MCA 100-16;
// the one for a departure that has to cross a runway is still to come.
public sealed record CrossRunway(string Runway, Frequency? Solo) : AtcInstruction;

/// <summary>The pilot cleared the runway and is taken to the apron: "taxie para o pátio" (Art. 134, II).</summary>
// TODO(fraseologia): the manual says "taxie para o pátio 1 via taxiway D"; the airport data has neither the apron nor the taxiways, so both are left out.
public sealed record TaxiToApron : AtcInstruction;

/// <summary>
/// "Decolado aos 26, chame o Controle ...": the Torre tells the aircraft it is airborne, with the minute it left the ground and the next
/// unit and its frequency (MCA 100-16, Art. 127). <paramref name="Approach"/> is the Controle of the airport, null when the data has none:
/// then only the minute is said, since who receives the flight there (an area center, say) is not in the data.
/// </summary>
// TODO(fraseologia): the minutes are said even though the first paragraph lets the DECEA waive the time at some aerodromes; the phrase of the
// Torre for a flight that goes to a unit the airport data does not have is not defined.
public sealed record AirborneNotice(int Minute, Frequency? Approach = null) : AtcInstruction;

/// <summary>A correct readback needs no answer (MCA 100-16 Art. 12): the controller stays silent.</summary>
public sealed record NoResponse : AtcInstruction;
