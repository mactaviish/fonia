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

/// <summary>A correct readback needs no answer (MCA 100-16 Art. 12): the controller stays silent.</summary>
public sealed record NoResponse : AtcInstruction;
