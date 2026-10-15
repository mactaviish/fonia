using Fonia.Core.Sim;

namespace Fonia.Core.Atc;

/// <summary>Why a snapshot is taken for the start of another flight.</summary>
public enum FlightChangeReason
{
    /// <summary>The aircraft is somewhere it could not have got to since the last reading: the simulator loaded another flight.</summary>
    Teleported,

    /// <summary>The registration is another one: the user changed aircraft.</summary>
    AnotherAircraft,
}

/// <summary>The snapshot that opens the new flight, and why it is one.</summary>
public sealed record FlightChange(AircraftState State, FlightChangeReason Reason);

/// <summary>
/// Notices when the simulator is no longer showing the flight the app started with, so that the app can follow it without the user
/// doing anything. It compares each snapshot with the one before: a jump of tens of kilometers in a reading cannot be flying; a few
/// kilometers after the long silence of a flight loading is a place reached by loading; a registration that changes is another aircraft.
/// A silence alone (a pause) is nothing. The first snapshot is only the baseline.
/// </summary>
public sealed class FlightChangeDetector
{
    // No aircraft covers this between two readings, a second or a few seconds apart.
    private const double ImpossibleJumpKm = 20;

    // A flight that loads leaves the simulator silent for a while; after that silence, a place this far away is not where it was.
    private static readonly TimeSpan LoadingSilence = TimeSpan.FromSeconds(8);
    private const double JumpAfterSilenceKm = 3;

    private AircraftState? _last;
    private string _registration = string.Empty;

    /// <returns>The change this snapshot shows, or null.</returns>
    public FlightChange? Observe(AircraftState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var previous = _last;
        var registration = Normalize(state.Registration);
        var reading = Classify(previous, state, registration);

        _last = state;

        if (registration.Length > 0)
        {
            _registration = registration;
        }

        return reading is { } reason ? new FlightChange(state, reason) : null;
    }

    private FlightChangeReason? Classify(AircraftState? previous, AircraftState state, string registration)
    {
        if (previous is null)
        {
            return null;
        }

        // An empty registration is a reading that has not settled yet (it may change right after a flight loads): not a change.
        if (registration.Length > 0 && _registration.Length > 0 && registration != _registration)
        {
            return FlightChangeReason.AnotherAircraft;
        }

        var km = Geo.DistanceKm(previous.LatitudeDegrees, previous.LongitudeDegrees, state.LatitudeDegrees, state.LongitudeDegrees);

        return km >= ImpossibleJumpKm || (km >= JumpAfterSilenceKm && state.Timestamp - previous.Timestamp >= LoadingSilence)
            ? FlightChangeReason.Teleported
            : null;
    }

    private static string Normalize(string registration) => new([.. registration.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);
}
