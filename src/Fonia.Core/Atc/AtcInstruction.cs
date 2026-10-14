using Fonia.Core.Sim;

namespace Fonia.Core.Atc;

/// <summary>
/// What the controller decided to instruct, as data. Spoken text is produced later
/// from phraseology templates, never here.
/// </summary>
public abstract record AtcInstruction;

/// <summary>Taxi to the holding point of <paramref name="Runway"/> (no detailed route yet).</summary>
public sealed record TaxiClearance(string Runway) : AtcInstruction;

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

/// <summary>"Decolado aos 26": the Torre tells the aircraft it is airborne, with the minute it left the ground (MCA 100-16, Art. 127).</summary>
// TODO(fraseologia): the manual follows it with "chame o Controle ..." (the next unit), which the airport data does not have; the minutes are
// said even though the first paragraph lets the DECEA waive the time at some aerodromes.
public sealed record AirborneNotice(int Minute) : AtcInstruction;

/// <summary>A correct readback needs no answer (MCA 100-16 Art. 12): the controller stays silent.</summary>
public sealed record NoResponse : AtcInstruction;
