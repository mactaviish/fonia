using Fonia.Core.Airports;

namespace Fonia.Data;

/// <summary>
/// The map of an airport kept on this computer. When there is a simulator to ask (<paramref name="inner"/>) its answer wins, because the scenery
/// may have changed, and is kept in <c>&lt;directory&gt;\&lt;ICAO&gt;.json</c>, the same file <c>--dump-layout</c> writes; when it gives none, or
/// there is no simulator (practising phraseology without it), the file kept before is used. With neither, there is no map. The file is only
/// ever written from what the simulator said, and stays on the user's computer.
/// </summary>
/// <param name="inner">The simulator's source; null when there is no simulator, and then only the file is used.</param>
/// <param name="directory">Where the layouts are kept (<c>%LOCALAPPDATA%\Fonia\layouts</c>).</param>
public sealed class CachedAirportLayoutSource(IAirportLayoutSource? inner, string directory) : IAirportLayoutSource
{
    public async Task<AirportLayout?> GetAsync(string icao, CancellationToken ct)
    {
        // The code becomes a file name: only letters and digits, so nothing can point outside the directory.
        if (!IsFileSafe(icao))
        {
            return null;
        }

        if (inner is not null)
        {
            AirportLayout? fresh = null;

            try
            {
                fresh = await inner.GetAsync(icao, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A source that fails is a source that has nothing: the file is the next best.
            }

            if (fresh is not null)
            {
                Save(icao, fresh);
                return fresh;
            }
        }

        return Load(icao);
    }

    private static bool IsFileSafe(string icao) => icao is { Length: >= 3 and <= 8 } && icao.All(char.IsAsciiLetterOrDigit);

    private string PathOf(string icao) => Path.Combine(directory, $"{icao.ToUpperInvariant()}.json");

    private void Save(string icao, AirportLayout layout)
    {
        try
        {
            Directory.CreateDirectory(directory);

            // Written aside and moved, so a file cut short by the app ending never replaces a good one.
            var temporary = PathOf(icao) + ".tmp";

            File.WriteAllText(temporary, AirportLayoutJson.Write(layout));
            File.Move(temporary, PathOf(icao), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AirportLayoutFormatException)
        {
            // Keeping it is a convenience: the layout in hand is used all the same.
        }
    }

    private AirportLayout? Load(string icao)
    {
        try
        {
            var path = PathOf(icao);

            if (!File.Exists(path))
            {
                return null;
            }

            var layout = AirportLayoutJson.Read(File.ReadAllText(path));

            return string.Equals(layout.Ident, icao, StringComparison.OrdinalIgnoreCase) ? layout : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AirportLayoutFormatException)
        {
            return null;
        }
    }
}
