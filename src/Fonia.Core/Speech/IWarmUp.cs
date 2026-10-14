namespace Fonia.Core.Speech;

/// <summary>
/// Something that is slow the first time it is used (loading a model), and can be made ready ahead of time, so that the
/// first thing the pilot says is not the one that pays for it.
/// </summary>
public interface IWarmUp
{
    /// <summary>Loads what is needed and runs it once. Throws what the first real use would throw (a missing model, for instance).</summary>
    Task WarmUpAsync(CancellationToken ct);
}
