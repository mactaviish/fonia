namespace Fonia.Speech.Tests;

internal static class FakeModelFiles
{
    /// <summary>
    /// A model file for a test: a catalog file gets the size of the real one (set without writing the bytes, so it is fast
    /// and takes no room), any other name gets one byte.
    /// </summary>
    public static void Create(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        stream.SetLength(ModelCatalog.Find(Path.GetFileName(path))?.Size ?? 1);
    }
}
