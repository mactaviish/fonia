namespace Fonia.Core.Atc;

/// <summary>Which of the controller's transmissions the pilot only acknowledges (or not at all), instead of reading them back.</summary>
public static class Acknowledgements
{
    /// <summary>
    /// True for what the manual does not list among what must be read back (Art. 12) and for what the controller says without being asked
    /// ("decolado aos ...", "no solo aos ...", "reporte perna base", "chame a Torre ..."): pilots acknowledge it anyway, in any words, and
    /// the controller takes it in silence.
    /// </summary>
    public static bool NeedsNoReadback(AtcInstruction instruction) =>
        instruction is HandoffToTower or HandoffToGround or ReportBase or ReportOnFinal or AirborneNotice or LandedNotice;
}
