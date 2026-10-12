using Fonia.Core.Sim;

namespace Fonia.Sim.Msfs;

/// <summary>
/// Collects the groups of SimVars (each arrives on its own schedule, on SimConnect.NET's threads) and
/// decides when they add up to a snapshot worth publishing. Thread-safe.
/// </summary>
/// <remarks>
/// <para>A snapshot is valid only when the simulation is running, every group has arrived since the last
/// start of the simulation, and the position is not the placeholder MSFS reports while a flight loads
/// (near 0°, 0°; it keeps reporting it for ~10 s after the "Sim" event says the simulation is running).</para>
/// <para>Motion is the heartbeat: every valid motion update is published. A change in any other group is
/// published immediately, so a new frequency or squawk does not wait for the next motion update;
/// repeats of an unchanged group publish nothing.</para>
/// </remarks>
internal sealed class MsfsSnapshotAssembler(TimeProvider timeProvider)
{
    // MSFS reports a position within this many degrees of (0, 0) while the flight is still loading.
    // No airport is there, so treating it as "not loaded yet" costs nothing.
    private const double PlaceholderRadiusDegrees = 0.05;

    private readonly Lock _gate = new();
    private bool _simRunning;
    private MotionData? _motion;
    private IdentityData? _identity;
    private RadioData? _radios;
    private TransponderData? _transponder;

    /// <summary>The "Sim" system event: <see langword="true"/> while a flight is running, <see langword="false"/> in menus and loading.</summary>
    /// <remarks>Every change (not a repeat of the same value) discards the groups received so far, so a reloaded flight never mixes in old values.</remarks>
    public void OnSimRunning(bool running)
    {
        lock (_gate)
        {
            if (_simRunning == running)
            {
                return;
            }

            _simRunning = running;
            ClearGroups();
        }
    }

    /// <summary>Forgets everything, for a brand new connection.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _simRunning = false;
            ClearGroups();
        }
    }

    public AircraftState? OnMotion(MotionData motion)
    {
        lock (_gate)
        {
            _motion = motion;
            return Build();
        }
    }

    public AircraftState? OnIdentity(IdentityData identity)
    {
        lock (_gate)
        {
            var changed = !Equals(_identity, identity);
            _identity = identity;
            return changed ? Build() : null;
        }
    }

    public AircraftState? OnRadios(RadioData radios)
    {
        lock (_gate)
        {
            var changed = _radios != radios;
            _radios = radios;
            return changed ? Build() : null;
        }
    }

    public AircraftState? OnTransponder(TransponderData transponder)
    {
        lock (_gate)
        {
            var changed = _transponder != transponder;
            _transponder = transponder;
            return changed ? Build() : null;
        }
    }

    // Must be called with _gate held.
    private void ClearGroups()
    {
        _motion = null;
        _identity = null;
        _radios = null;
        _transponder = null;
    }

    // Must be called with _gate held.
    private AircraftState? Build()
    {
        if (!_simRunning || _motion is not { } motion || _identity is not { } identity || _radios is not { } radios || _transponder is not { } transponder)
        {
            return null;
        }

        if (!IsLoadedPosition(motion))
        {
            return null;
        }

        return new AircraftState(
            Timestamp: timeProvider.GetUtcNow(),
            LatitudeDegrees: motion.LatitudeDegrees,
            LongitudeDegrees: motion.LongitudeDegrees,
            AltitudeMslFeet: motion.AltitudeMslFeet,
            AltitudeAglFeet: motion.AltitudeAglFeet,
            GroundSpeedKnots: motion.GroundSpeedKnots,
            HeadingMagneticDegrees: motion.HeadingMagneticDegrees,
            OnGround: motion.SimOnGround != 0,
            Com1: MsfsConversions.ToRadio(radios.Com1ActiveHz, radios.Com1StandbyHz),
            Com2: MsfsConversions.ToRadio(radios.Com2ActiveHz, radios.Com2StandbyHz),
            TransponderCode: MsfsConversions.ToSquawk(transponder.CodeBco16),
            Registration: MsfsConversions.ToRegistration(identity.AtcId),
            AircraftType: MsfsConversions.ToAircraftType(identity.AtcModel, identity.Title));
    }

    private static bool IsLoadedPosition(MotionData motion)
    {
        return double.IsFinite(motion.LatitudeDegrees)
            && double.IsFinite(motion.LongitudeDegrees)
            && (Math.Abs(motion.LatitudeDegrees) >= PlaceholderRadiusDegrees || Math.Abs(motion.LongitudeDegrees) >= PlaceholderRadiusDegrees);
    }
}
