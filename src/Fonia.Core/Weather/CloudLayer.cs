namespace Fonia.Core.Weather;

/// <summary>
/// A layer of clouds of a METAR. The report gives the cover in categories, so <paramref name="Octas"/> is the middle of the category
/// (FEW 2, SCT 4, BKN 6, OVC 8), which is what the ATIS says (ICA 100-37, Art. 774: the quantity in eighths).
/// </summary>
/// <param name="BaseFeet">The height of the base in feet above the aerodrome.</param>
/// <param name="Cumulonimbus">Whether the layer is cumulonimbus: the only type the ATIS names.</param>
public sealed record CloudLayer(int Octas, int BaseFeet, bool Cumulonimbus = false);
