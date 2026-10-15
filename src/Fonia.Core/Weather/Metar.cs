using Fonia.Core.Sim;

namespace Fonia.Core.Weather;

/// <summary>The part of a METAR the controller needs: the surface wind, the temperature and the altimeter setting.</summary>
/// <param name="Wind">The wind as reported, in degrees <b>true</b> (the controller says it magnetic, see <see cref="MagneticVariation"/>); null when the report has none or it is variable.</param>
/// <param name="VariableWind">The report says the wind has no direction ("VRB"): there is a speed but nothing to say about where it comes from.</param>
/// <param name="QnhHectopascals">The altimeter setting in hPa (the METAR's "Q1018", or "A2992" turned into hPa), or null when the report has none.</param>
/// <param name="VisibilityMeters">The prevailing visibility in meters ("9999" is 10 km or more, and so is CAVOK), or null when the report has none in meters.</param>
/// <param name="Raw">The report as it came, for the user to read.</param>
public sealed record Metar(string Station, Wind? Wind, int? TemperatureCelsius, int? QnhHectopascals, bool VariableWind, string Raw, int? VisibilityMeters = null);
