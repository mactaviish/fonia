using System.Text.Json;
using System.Text.Json.Serialization;
using Fonia.Core.Sim;

namespace Fonia.Sim.Mock.Telemetry;

/// <summary>Stores a <see cref="Frequency"/> as its integer number of kHz (118.275 MHz = 118275).</summary>
internal sealed class FrequencyJsonConverter : JsonConverter<Frequency>
{
    public override Frequency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            return new Frequency(reader.GetInt32());
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JsonException("A frequency must be a positive number of kHz.", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Frequency value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.KHz);
    }
}
