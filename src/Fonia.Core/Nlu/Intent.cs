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

/// <summary>
/// The pilot reports being at the holding point ("no ponto de espera da pista 15"), which is what hands the flight from the Solo
/// to the Torre. It is not the readback of the taxi clearance, which repeats the instruction ("para o ponto de espera da 15").
/// </summary>
/// <param name="Runway">The runway the pilot named, or null when none was said.</param>
public sealed record ReportHoldingShort(string? Callsign, string? Runway) : Intent(Callsign);

/// <summary>
/// The pilot is ready to depart ("pronto para partida", "solicito decolagem", with or without the holding point): the Torre lines
/// the flight up, or, if it already is, clears the takeoff (MCA 100-16, Art. 126, I).
/// </summary>
public sealed record RequestDeparture(string? Callsign, string? Runway) : Intent(Callsign);

/// <summary>The pilot reports being lined up on the runway ("alinhado").</summary>
public sealed record ReportLinedUp(string? Callsign, string? Runway) : Intent(Callsign);

/// <summary>The pilot reports starting the takeoff ("decolando"): the Torre has nothing to say to it.</summary>
public sealed record ReportTakingOff(string? Callsign) : Intent(Callsign);

/// <summary>The pilot asks for instructions to land ("instruções para pouso", "solicito entrada no circuito"): MCA 100-16, Art. 128.</summary>
public sealed record RequestLanding(string? Callsign) : Intent(Callsign);

/// <summary>The pilot reports the downwind leg ("perna do vento"): Art. 130, I.</summary>
public sealed record ReportDownwind(string? Callsign) : Intent(Callsign);

/// <summary>The pilot reports the base leg ("perna base"): Art. 131, I.</summary>
public sealed record ReportBaseLeg(string? Callsign) : Intent(Callsign);

/// <summary>The pilot reports final ("final", "na final", "final longa"): Art. 132, I.</summary>
public sealed record ReportFinal(string? Callsign) : Intent(Callsign);

/// <summary>The pilot reports a go-around ("arremetendo", "aproximação perdida"): Art. 136.</summary>
public sealed record ReportGoingAround(string? Callsign) : Intent(Callsign);

/// <summary>Speech that could not be interpreted.</summary>
public sealed record Unknown(string RawText) : Intent((string?)null);
