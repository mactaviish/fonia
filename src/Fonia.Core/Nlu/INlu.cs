using Fonia.Core.Atc;

namespace Fonia.Core.Nlu;

/// <summary>Turns what the pilot said (as text) into an <see cref="Intent"/>. It interprets; it never decides the answer.</summary>
public interface INlu
{
    Intent Interpret(string? text, FlightContext flight);
}
