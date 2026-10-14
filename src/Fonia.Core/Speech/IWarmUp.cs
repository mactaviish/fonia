namespace Fonia.Core.Speech;

/// <summary>
/// Something that is slow the first time it is used (loading a model), and can be made ready ahead of time, so that the
/// first thing the pilot says is not the one that pays for it.
/// </summary>
public interface IWarmUp
{
    /// <summary>Loads what is needed and runs it once. Throws what the first real use would throw (a missing model, for instance).</summary>
    /// <param name="context">What the first real use will be like (the callsign and the station), so that nothing has to be redone for it.</param>
    Task WarmUpAsync(TranscriptionContext context, CancellationToken ct);
}
