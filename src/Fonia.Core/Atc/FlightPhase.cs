namespace Fonia.Core.Atc;

/// <summary>Coarse phase of a flight. Grows with the milestones that need it.</summary>
public enum FlightPhase
{
    Parked = 0,
    PushbackApproved,
    StartupApproved,
    Taxiing,

    /// <summary>The Solo reported the holding point and passed the flight to the Torre: from here the Torre talks.</summary>
    HandedToTower,

    /// <summary>The Torre told the flight to line up and wait on the runway.</summary>
    LineUpCleared,

    /// <summary>The Torre cleared the takeoff.</summary>
    TakeoffCleared,

    /// <summary>The telemetry showed the aircraft left the ground after the clearance, and the Torre said so.</summary>
    Airborne,

    /// <summary>The Torre cleared the flight to the traffic circuit and waits for the downwind report.</summary>
    InCircuit,

    /// <summary>The pilot reported the downwind and was told to report the base leg.</summary>
    OnDownwind,

    /// <summary>The pilot reported the base leg and was told to report final, or was cleared for the straight-in approach and told the same.</summary>
    OnBase,

    /// <summary>The Torre cleared the landing.</summary>
    LandingCleared,

    /// <summary>The telemetry showed the aircraft on the ground after the clearance, and the Torre said so.</summary>
    Landed,
}
