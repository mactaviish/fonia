using Fonia.Core.Atc;
using Fonia.Core.Nlu;

namespace Fonia.Core.Pipeline;

/// <summary>
/// Which exchanges are worth keeping to improve the app: the ones where the controller could not follow the pilot. The reasons are
/// short names that go into the file of cases. What the app understood wrongly without knowing cannot be told from here: the pilot
/// marks those.
/// </summary>
public static class CaseReasons
{
    /// <summary>The controller said "repita": the NLU found nothing it knew.</summary>
    public const string NotUnderstood = "nao-entendido";

    /// <summary>The readback was corrected ("negativo"): the pilot may be wrong, or the recognizer may have written it wrong.</summary>
    public const string Corrected = "negativo";

    /// <summary>A request without the aircraft's callsign (or with another one): "repita o indicativo".</summary>
    public const string NoCallsign = "sem-indicativo";

    /// <summary>Only the callsign was heard while a readback was due: probably the recognizer dropped the rest.</summary>
    public const string GoAheadInsteadOfReadback = "so-o-indicativo";

    /// <summary>The pilot said the controller got it wrong (an exchange the app thought it had understood).</summary>
    public const string Marked = "marcado";

    public static IReadOnlyList<string> All { get; } = [NotUnderstood, Corrected, NoCallsign, GoAheadInsteadOfReadback, Marked];

    /// <param name="before">The flight before the pilot spoke: what was waiting for a readback.</param>
    /// <returns>The reasons this exchange is a case, none when it went well.</returns>
    public static IReadOnlyList<string> Of(FlightContext before, Intent intent, AtcInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(instruction);

        return instruction switch
        {
            SayAgainRequest => [NotUnderstood],
            RequestReadbackCorrection => [Corrected],
            RequestCallsign => [NoCallsign],
            GoAhead when intent is CallsignOnly && before.PendingReadback is not null => [GoAheadInsteadOfReadback],
            _ => [],
        };
    }
}
