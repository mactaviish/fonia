namespace Fonia.Core.Airports;

/// <summary>
/// An airport that uses one runway to land and another to take off, in the same direction (Guarulhos with its 10 in use: lands on the 10R, takes
/// off from the 10L). Which direction is in use is decided as for any airport (the wind, or the user); the rule says which runway does what.
/// </summary>
/// <param name="Arrival">The runway end that receives the landings ("10R").</param>
/// <param name="Departure">The runway end the takeoffs use ("10L").</param>
public sealed record RunwayUseRule(string Arrival, string Departure);
