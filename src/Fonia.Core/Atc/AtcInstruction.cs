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
