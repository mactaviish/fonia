using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Fonia.Speech;

/// <summary>
/// Reads the voices of Kokoro, which come in one numpy ".npz" file: a zip with one ".npy" table per voice, 510 rows (one
/// per number of phonemes) of 256 numbers (the "style" that makes the voice that voice). Only what is needed to read that
/// layout, so that no numpy library is needed.
/// </summary>
internal static partial class NpzVoiceReader
{
    public const int Rows = 510;
    public const int StyleSize = 256;

    private static readonly byte[] Magic = [0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y'];

    /// <summary>All the rows of a voice, one after the other.</summary>
    /// <exception cref="InvalidDataException">The file is not a voices file, or the voice's table is not what is expected.</exception>
    /// <exception cref="KeyNotFoundException">The voice is not in the file.</exception>
    public static float[] ReadStyles(string path, string voiceId)
    {
        ZipArchive zip;

        try
        {
            zip = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"'{path}' não é um arquivo de vozes do Kokoro (esperado: voices-v1.0.bin).", ex);
        }

        using (zip)
        {
            var entry = zip.GetEntry(voiceId + ".npy")
                ?? throw new KeyNotFoundException(
                    $"A voz '{voiceId}' não está em '{path}'. Vozes do arquivo: {string.Join(", ", zip.Entries.Select(e => Path.GetFileNameWithoutExtension(e.Name)).Order().Take(60))}.");

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            return ParseNpy(memory.ToArray(), voiceId);
        }
    }

    /// <summary>The row of the table for a sequence of <paramref name="phonemeCount"/> phonemes (the last row for a longer one).</summary>
    public static ReadOnlySpan<float> StyleFor(float[] styles, int phonemeCount)
    {
        var row = Math.Clamp(phonemeCount, 1, Rows) - 1;

        return styles.AsSpan(row * StyleSize, StyleSize);
    }

    private static float[] ParseNpy(byte[] data, string voiceId)
    {
        if (data.Length < 10 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException($"A tabela da voz '{voiceId}' não é um arquivo numpy.");
        }

        var major = data[6];
        var headerLength = major == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8, 2)) : (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4));
        var headerStart = major == 1 ? 10 : 12;

        if (headerStart + headerLength > data.Length)
        {
            throw new InvalidDataException($"A tabela da voz '{voiceId}' está incompleta.");
        }

        var header = Encoding.ASCII.GetString(data, headerStart, headerLength);
        var shape = ShapePattern().Match(header);
        var expected = (Rows.ToString(CultureInfo.InvariantCulture), "1", StyleSize.ToString(CultureInfo.InvariantCulture));

        if (!header.Contains("'<f4'", StringComparison.Ordinal) || header.Contains("'fortran_order': True", StringComparison.Ordinal)
            || !shape.Success || (shape.Groups[1].Value, shape.Groups[2].Value, shape.Groups[3].Value) != expected)
        {
            throw new InvalidDataException($"A tabela da voz '{voiceId}' não tem o formato esperado (float32, {Rows}x1x{StyleSize}): {header.Trim()}");
        }

        var values = Rows * StyleSize;
        var dataStart = headerStart + headerLength;

        if (data.Length - dataStart < values * sizeof(float))
        {
            throw new InvalidDataException($"A tabela da voz '{voiceId}' está incompleta.");
        }

        var styles = new float[values];
        Buffer.BlockCopy(data, dataStart, styles, 0, values * sizeof(float));
        return styles;
    }

    [GeneratedRegex(@"'shape':\s*\((\d+),\s*(\d+),\s*(\d+)\)")]
    private static partial Regex ShapePattern();
}
