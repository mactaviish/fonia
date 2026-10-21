namespace Fonia.Core.Phraseology;

/// <summary>
/// The callsign of an airline flight: the designator of the company plus the flight number ("GLO1234"). The simulator gives the airline as the
/// name the pilot set for the radio (MSFS "ATC AIRLINE", which is the telephony: "Gol") and the number apart (MSFS "ATC FLIGHT NUMBER"); the
/// table of telephonies (<c>data/telephony.csv</c>) says which designator the name is.
/// </summary>
public static class AirlineCallsign
{
    /// <summary>
    /// The callsign, or null when the airline is not a telephony of the table or the flight number has no digits: then the registration is the
    /// callsign, as it always was.
    /// </summary>
    public static string? From(string? airline, string? flightNumber)
    {
        var number = new string([.. (flightNumber ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

        if (number.Length == 0 || !number.Any(char.IsAsciiDigit))
        {
            return null;
        }

        var tokens = SpokenTokens.Tokenize(airline);

        return tokens.Count > 0 && Telephony.TryRead(tokens, 0, out var designator, out var words) && words == tokens.Count
            ? designator + number
            : null;
    }
}
