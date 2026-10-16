using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Core.Atc;

/// <summary>
/// What the simulator shows of the aircraft, read against the clearance the controller gave: whether the aircraft is already doing it, and
/// whether it is where the clearance says. A pilot who is rolling on the runway has heard the takeoff clearance, whatever the readback was; one
/// lined up on the runway has not misheard which runway it is. Pure: the same aircraft and clearance always give the same answer.
/// </summary>
public static class Situation
{
    // From this speed on the ground the aircraft is rolling for the takeoff (or has landed and is rolling out), not holding.
    private const double RollingKnots = 30;

    // From this speed on the ground the aircraft is taxiing.
    private const double TaxiingKnots = 3;

    // How far the heading may be from the runway for the aircraft to be "on" it.
    private const double AlignedDegrees = 30;

    // "On the approach or in the circuit": in the air and this near the airport.
    private const double NearAirportKm = 30;

    /// <summary>True when the aircraft is already doing what was cleared, so there is nothing left to read back.</summary>
    public static bool AlreadyDoing(AtcInstruction cleared, AircraftState? aircraft)
    {
        if (aircraft is null)
        {
            return false;
        }

        return cleared switch
        {
            // Rolling for the takeoff or already flying.
            TakeoffClearance or LineUpAndWait => !aircraft.OnGround || aircraft.GroundSpeedKnots >= RollingKnots,

            // A landing clearance that is still pending with the aircraft on the ground: it has landed.
            LandingClearance => aircraft.OnGround,

            // A taxi clearance with the aircraft already moving on the ground.
            TaxiClearance => aircraft.OnGround && aircraft.GroundSpeedKnots >= TaxiingKnots,
            _ => false,
        };
    }

    /// <summary>True when the aircraft is where the clearance says: lined up on its runway, or on the approach, or in the circuit.</summary>
    public static bool Confirms(AtcInstruction cleared, AircraftState? aircraft, AirportContext airport)
    {
        ArgumentNullException.ThrowIfNull(airport);

        if (aircraft is null)
        {
            return false;
        }

        return cleared switch
        {
            LineUpAndWait lineUp => OnTheRunway(aircraft, airport, lineUp.Runway),
            TakeoffClearance takeoff => OnTheRunway(aircraft, airport, takeoff.Runway),
            LandingClearance or CircuitClearance => !aircraft.OnGround && airport.Airport.DistanceKmTo(aircraft.LatitudeDegrees, aircraft.LongitudeDegrees) <= NearAirportKm,

            // The runway of a taxi cannot be told from where the aircraft is: only what it says counts.
            _ => false,
        };
    }

    // On the ground, holding, and pointing along the runway (the data has the runways as headings, not as positions).
    private static bool OnTheRunway(AircraftState aircraft, AirportContext airport, string runway)
    {
        if (!aircraft.OnGround || aircraft.GroundSpeedKnots >= RollingKnots)
        {
            return false;
        }

        var end = airport.Airport.Runways
            .SelectMany(r => r.Ends)
            .FirstOrDefault(e => RunwayDesignator.AreSame(e.Designator, runway));

        if (end?.MagneticHeadingDegrees is not { } heading)
        {
            return false;
        }

        var difference = Math.Abs(((aircraft.HeadingMagneticDegrees - heading) % 360 + 540) % 360 - 180);

        return difference <= AlignedDegrees;
    }
}
