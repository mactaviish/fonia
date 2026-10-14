using System.Buffers.Binary;
using System.Text;

namespace Fonia.Core.Audio;

/// <summary>Wraps raw PCM in a canonical 44-byte WAV header, for saving a recording or handing it to a tool.</summary>
public static class WavEncoder
{
    private const int HeaderSize = 44;

    public static byte[] Encode(RecordedSpeech speech)
    {
        ArgumentNullException.ThrowIfNull(speech);

        var format = speech.Format;
        var wav = new byte[HeaderSize + speech.Pcm.Length];
        var span = wav.AsSpan();

        Encoding.ASCII.GetBytes("RIFF", span[0..4]);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..8], 36 + speech.Pcm.Length);
        Encoding.ASCII.GetBytes("WAVEfmt ", span[8..16]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..20], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..22], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..24], (short)format.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..28], format.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..32], format.BytesPerSecond);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..34], (short)format.BytesPerFrame);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..36], (short)format.BitsPerSample);
        Encoding.ASCII.GetBytes("data", span[36..40]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..44], speech.Pcm.Length);
        speech.Pcm.CopyTo(span[HeaderSize..]);

        return wav;
    }
}
