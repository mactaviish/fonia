namespace Fonia.Core.Nlu;

/// <summary>
/// Structured meaning of what the pilot said. The NLU only interprets speech; it never
/// decides the ATC answer. <see cref="Callsign"/> is normalized by the NLU (e.g. "PRABC").
/// </summary>
public abstract record Intent(string? Callsign);

/// <summary>Pilot asks to start the engines. <paramref name="Stand"/> is the parking position they gave, if any ("box 3").</summary>
public sealed record RequestStartup(string? Callsign, string? Stand) : Intent(Callsign);

/// <summary>
/// Pilot asks to be pushed back. <paramref name="WithStartup"/> is true when they asked for pushback and engine
/// start together, which is how it is done in practice.
/// </summary>
public sealed record RequestPushback(string? Callsign, bool WithStartup, string? Stand) : Intent(Callsign);

public sealed record RequestTaxi(string? Callsign, string? Runway) : Intent(Callsign);

/// <summary>Pilot read back an instruction; <paramref name="Text"/> is the normalized readback.</summary>
public sealed record Readback(string? Callsign, string Text) : Intent(Callsign);

public sealed record SayAgain(string? Callsign) : Intent(Callsign);

/// <summary>The pilot only called the controller and said their callsign ("Solo Curitiba, PRABC"), without a request.</summary>
public sealed record CallsignOnly(string? Callsign) : Intent(Callsign);

/// <summary>Speech that could not be interpreted.</summary>
public sealed record Unknown(string RawText) : Intent((string?)null);
