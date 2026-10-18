namespace Fonia.Core.Atc;

/// <summary>Who answers the pilot at an airport, in the order of what the airport really has.</summary>
public enum ServiceUnit
{
    /// <summary>No service: nobody answers on the frequency.</summary>
    None = 0,

    /// <summary>Rádio (AFIS): flight information only, no clearances.</summary>
    Afis,

    /// <summary>Torre: also handles what the Solo would, when the airport has no Solo.</summary>
    Tower,

    /// <summary>Solo.</summary>
    Ground,

    /// <summary>
    /// Controle (the approach). Never what an airport starts with: only the pilot tuning its frequency brings it, and it
    /// sends what belongs to the Torre to the Torre.
    /// </summary>
    Approach,
}
