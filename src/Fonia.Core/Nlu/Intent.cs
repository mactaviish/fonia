namespace Fonia.Core.Nlu;

/// <summary>
/// Structured meaning of what the pilot said. The NLU only interprets speech; it never
/// decides the ATC answer. <see cref="Callsign"/> is normalized by the NLU (e.g. "PRABC").
/// </summary>
public abstract record Intent(string? Callsign);

public sealed record RequestTaxi(string? Callsign, string? Runway) : Intent(Callsign);

/// <summary>Pilot read back an instruction; <paramref name="Text"/> is the normalized readback.</summary>
public sealed record Readback(string? Callsign, string Text) : Intent(Callsign);

public sealed record SayAgain(string? Callsign) : Intent(Callsign);

/// <summary>Speech that could not be interpreted.</summary>
public sealed record Unknown(string RawText) : Intent((string?)null);
