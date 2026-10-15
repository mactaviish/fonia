namespace Fonia.Core.Sim;

/// <summary>Distances on the Earth, for what is near what.</summary>
public static class Geo
{
    private const double EarthRadiusKm = 6371.0;

    /// <summary>The distance in kilometers along the great circle between two positions (haversine, a sphere of 6371 km).</summary>
    public static double DistanceKm(double latitude1Degrees, double longitude1Degrees, double latitude2Degrees, double longitude2Degrees)
    {
        var lat1 = latitude1Degrees * Math.PI / 180;
        var lat2 = latitude2Degrees * Math.PI / 180;
        var dLat = lat2 - lat1;
        var dLon = (longitude2Degrees - longitude1Degrees) * Math.PI / 180;
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2)) + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));

        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
