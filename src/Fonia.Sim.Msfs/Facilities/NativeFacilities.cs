using System.Reflection;
using System.Runtime.InteropServices;

namespace Fonia.Sim.Msfs;

/// <summary>
/// The two calls of SimConnect that SimConnect.NET 0.2.2 does not offer (its facility functions are internal to it): defining what to ask of an
/// airport and asking. They use the handle of the library's own connection. The DLL is the one the package already brings next to the app.
/// </summary>
internal static partial class NativeFacilities
{
    private const string Library = "SimConnect";

    private static int _resolverSet;

    /// <summary>SimConnect_AddToFacilityDefinition: adds one field name ("OPEN AIRPORT", "LATITUDE", ...) to a definition. Returns an HRESULT, 0 is success.</summary>
    [LibraryImport(Library, EntryPoint = "SimConnect_AddToFacilityDefinition", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddToFacilityDefinition(IntPtr handle, uint defineId, string fieldName);

    /// <summary>SimConnect_RequestFacilityData: asks for the airport of that code with that definition. Returns an HRESULT, 0 is success.</summary>
    [LibraryImport(Library, EntryPoint = "SimConnect_RequestFacilityData", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int RequestFacilityData(IntPtr handle, uint defineId, uint requestId, string icao, string? region);

    /// <summary>Makes sure "SimConnect" is found where the package puts it, whichever way the app was started. Safe to call many times.</summary>
    internal static void EnsureResolver()
    {
        if (Interlocked.Exchange(ref _resolverSet, 1) == 0)
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeFacilities).Assembly, Resolve);
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library)
        {
            return IntPtr.Zero;
        }

        if (NativeLibrary.TryLoad(name, assembly, searchPath, out var found))
        {
            return found;
        }

        foreach (var path in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "SimConnect.dll"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "SimConnect.dll"),
        })
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out found))
            {
                return found;
            }
        }

        return IntPtr.Zero;
    }
}
