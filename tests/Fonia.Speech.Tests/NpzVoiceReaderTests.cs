using System.IO.Compression;
using System.Text;

namespace Fonia.Speech.Tests;

public sealed class NpzVoiceReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fonia-npz-" + Guid.NewGuid().ToString("N"));

    public NpzVoiceReaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    // A numpy ".npy" file: the magic, the version, the header as text, and the numbers.
    private static byte[] Npy(float[] values, string shape = "(510, 1, 256)", string descr = "<f4", bool fortran = false)
    {
        var header = $"{{'descr': '{descr}', 'fortran_order': {(fortran ? "True" : "False")}, 'shape': {shape}, }}";
        header = header.PadRight(((header.Length + 10 + 63) / 64 * 64) - 11) + "\n";

        using var stream = new MemoryStream();
        stream.Write([0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', 1, 0]);
        stream.Write(BitConverter.GetBytes((ushort)header.Length));
        stream.Write(Encoding.ASCII.GetBytes(header));

        foreach (var value in values)
        {
            stream.Write(BitConverter.GetBytes(value));
        }

        return stream.ToArray();
    }

    private string Npz(params (string Name, byte[] Data)[] entries)
    {
        var path = Path.Combine(_directory, "voices.bin");
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);

        foreach (var (name, data) in entries)
        {
            using var entry = zip.CreateEntry(name + ".npy", CompressionLevel.NoCompression).Open();
            entry.Write(data);
        }

        return path;
    }

    private static float[] Styles(float marker) => Enumerable.Range(0, 510 * 256).Select(i => marker + (i / 256)).ToArray();

    [Fact]
    public void It_reads_the_styles_of_the_voice_asked_for()
    {
        var path = Npz(("pf_dora", Npy(Styles(0))), ("pm_alex", Npy(Styles(1000))));

        var alex = NpzVoiceReader.ReadStyles(path, "pm_alex");

        alex.Length.ShouldBe(510 * 256);
        alex[0].ShouldBe(1000f);
        alex[(509 * 256) + 255].ShouldBe(1509f);
    }

    [Fact]
    public void Each_row_is_the_style_for_a_number_of_phonemes()
    {
        var styles = Styles(0);

        NpzVoiceReader.StyleFor(styles, 1).ToArray().ShouldAllBe(v => v == 0f);
        NpzVoiceReader.StyleFor(styles, 100).ToArray().ShouldAllBe(v => v == 99f);
        NpzVoiceReader.StyleFor(styles, 510).ToArray().ShouldAllBe(v => v == 509f);
        NpzVoiceReader.StyleFor(styles, 5000).ToArray().ShouldAllBe(v => v == 509f, "longer than the table: the last row");
        NpzVoiceReader.StyleFor(styles, 100).Length.ShouldBe(256);
    }

    [Fact]
    public void A_voice_that_is_not_in_the_file_is_an_error_that_lists_the_ones_that_are()
    {
        var path = Npz(("pf_dora", Npy(Styles(0))));

        var error = Should.Throw<KeyNotFoundException>(() => NpzVoiceReader.ReadStyles(path, "pm_zzz"));

        error.Message.ShouldContain("pm_zzz");
        error.Message.ShouldContain("pf_dora");
    }

    [Theory]
    [InlineData("(510, 256)")]
    [InlineData("(100, 1, 256)")]
    public void A_table_of_another_shape_is_refused(string shape)
    {
        var path = Npz(("pf_dora", Npy(new float[10], shape)));

        Should.Throw<InvalidDataException>(() => NpzVoiceReader.ReadStyles(path, "pf_dora")).Message.ShouldContain("pf_dora");
    }

    [Fact]
    public void Numbers_of_another_kind_are_refused()
    {
        var path = Npz(("pf_dora", Npy(Styles(0), descr: "<f8")));

        Should.Throw<InvalidDataException>(() => NpzVoiceReader.ReadStyles(path, "pf_dora"));
    }

    [Fact]
    public void A_file_that_is_not_a_zip_is_refused_with_a_clear_message()
    {
        var path = Path.Combine(_directory, "voices.bin");
        File.WriteAllText(path, "not a voices file");

        Should.Throw<InvalidDataException>(() => NpzVoiceReader.ReadStyles(path, "pf_dora"));
    }

    [Fact]
    public void A_truncated_table_is_refused()
    {
        var data = Npy(Styles(0));
        var path = Npz(("pf_dora", data[..(data.Length - 1000)]));

        Should.Throw<InvalidDataException>(() => NpzVoiceReader.ReadStyles(path, "pf_dora"));
    }
}
