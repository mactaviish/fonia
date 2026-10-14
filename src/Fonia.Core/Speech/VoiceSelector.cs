namespace Fonia.Core.Speech;

/// <summary>
/// Chooses the voice of each station ("SOLO Curitiba", "TORRE Curitiba"). By default the choice is random, so that
/// each session sounds different, but a station keeps its voice for the whole session, and a station that is not the
/// first gets a voice no other station has while there are voices left. A fixed voice, when the user picks one, is used by all.
/// </summary>
public sealed class VoiceSelector
{
    private readonly Voice[] _pool;
    private readonly Voice? _fixed;
    private readonly Random _random;
    private readonly Dictionary<string, Voice> _byStation = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <param name="pool">The voices that may be used.</param>
    /// <param name="fixedVoiceId">The voice the user picked, or null for a random one per station.</param>
    /// <param name="random">Where the randomness comes from; a fixed seed makes tests repeatable.</param>
    /// <exception cref="ArgumentException">The pool is empty, or the fixed voice is not in it.</exception>
    public VoiceSelector(IReadOnlyList<Voice> pool, string? fixedVoiceId = null, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(pool);

        if (pool.Count == 0)
        {
            throw new ArgumentException("There must be at least one voice.", nameof(pool));
        }

        _pool = [.. pool];
        _random = random ?? Random.Shared;

        if (!string.IsNullOrWhiteSpace(fixedVoiceId))
        {
            _fixed = _pool.FirstOrDefault(v => string.Equals(v.Id, fixedVoiceId.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(
                    $"The voice '{fixedVoiceId}' does not exist. Available: {string.Join(", ", _pool.Select(v => v.Id))}.",
                    nameof(fixedVoiceId));
        }
    }

    /// <summary>The voice of a station (any text that tells stations apart, such as "SOLO SBCT").</summary>
    public Voice For(string station)
    {
        ArgumentNullException.ThrowIfNull(station);

        if (_fixed is not null)
        {
            return _fixed;
        }

        lock (_gate)
        {
            if (_byStation.TryGetValue(station, out var voice))
            {
                return voice;
            }

            var unused = _pool.Where(v => !_byStation.ContainsValue(v)).ToArray();
            var candidates = unused.Length > 0 ? unused : _pool;
            voice = candidates[_random.Next(candidates.Length)];
            _byStation[station] = voice;
            return voice;
        }
    }
}
