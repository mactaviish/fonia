using System.Buffers.Binary;
using System.Text;

namespace Fonia.Core.Audio;

/// <summary>Reads a PCM WAV file into a <see cref="RecordedSpeech"/>. It does not convert: the format is whatever the file has.</summary>
public static class WavDecoder
{
    private const int PcmTag = 1;
    private const int ExtensibleTag = 0xFFFE;

    /// <exception cref="InvalidDataException">The bytes are not an uncompressed PCM WAV file.</exception>
    public static RecordedSpeech Decode(byte[] wav)
    {
        ArgumentNullException.ThrowIfNull(wav);

        if (wav.Length < 12 || Tag(wav, 0) != "RIFF" || Tag(wav, 8) != "WAVE")
        {
            throw new InvalidDataException("O arquivo não é um WAV (falta o cabeçalho RIFF/WAVE).");
        }

        AudioFormat? format = null;
        var position = 12;

        while (position + 8 <= wav.Length)
        {
            var id = Tag(wav, position);
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(position + 4, 4));
            var body = position + 8;
            var available = wav.Length - body;
            var size = (int)Math.Min(declared, (uint)available);

            if (id == "fmt ")
            {
                format = ReadFormat(wav, body, size);
            }
            else if (id == "data")
            {
                if (format is null)
                {
                    throw new InvalidDataException("O arquivo WAV tem os dados antes do formato.");
                }

                // A recorder that crashed, or a stream of unknown length, leaves a size that is too big: read to the end.
                var length = size - (size % format.BytesPerFrame);
                return new RecordedSpeech(format, wav.AsSpan(body, length).ToArray());
            }

            position = Math.Min(body + size + (size % 2), wav.Length); // chunks are padded to an even size
        }

        throw new InvalidDataException("O arquivo WAV não tem o trecho de dados.");
    }

    private static AudioFormat ReadFormat(byte[] wav, int body, int length)
    {
        if (length < 16)
        {
            throw new InvalidDataException("O arquivo WAV tem um cabeçalho de formato incompleto.");
        }

        var tag = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body, 2));

        // Extensible files (more than 2 channels or 24+ bits) carry the real tag deeper; only plain PCM is read here.
        if (tag != PcmTag && tag != ExtensibleTag)
        {
            throw new InvalidDataException("O WAV não é PCM sem compressão (use um WAV PCM de 16 bits).");
        }

        var channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 2, 2));
        var rate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(body + 4, 4));
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 14, 2));

        try
        {
            return new AudioFormat(rate, channels, bits);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException("O arquivo WAV tem um formato inválido.", ex);
        }
    }

    private static string Tag(byte[] wav, int offset) => Encoding.ASCII.GetString(wav, offset, 4);
}
