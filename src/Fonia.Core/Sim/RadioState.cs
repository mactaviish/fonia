namespace Fonia.Core.Sim;

/// <summary>Active and standby frequency of one COM radio.</summary>
public sealed record RadioState(Frequency Active, Frequency Standby);
