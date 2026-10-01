using Siniestros360.LocationSimulator.Models;

namespace Siniestros360.LocationSimulator.Services;

public static class GeoMath
{
    private const double EarthRadiusMeters = 6_371_000;

    public static double DistanceMeters(GeoPoint start, GeoPoint end)
    {
        var latitudeDelta = ToRadians(end.Latitude - start.Latitude);
        var longitudeDelta = ToRadians(end.Longitude - start.Longitude);
        var startLatitude = ToRadians(start.Latitude);
        var endLatitude = ToRadians(end.Latitude);
        var haversine = Math.Pow(Math.Sin(latitudeDelta / 2), 2) +
                        Math.Cos(startLatitude) * Math.Cos(endLatitude) *
                        Math.Pow(Math.Sin(longitudeDelta / 2), 2);

        return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(haversine));
    }

    public static GeoPoint Interpolate(GeoPoint start, GeoPoint end, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        return new GeoPoint(
            start.Latitude + ((end.Latitude - start.Latitude) * fraction),
            start.Longitude + ((end.Longitude - start.Longitude) * fraction));
    }

    public static double BearingDegrees(GeoPoint start, GeoPoint end)
    {
        var startLatitude = ToRadians(start.Latitude);
        var endLatitude = ToRadians(end.Latitude);
        var longitudeDelta = ToRadians(end.Longitude - start.Longitude);
        var y = Math.Sin(longitudeDelta) * Math.Cos(endLatitude);
        var x = Math.Cos(startLatitude) * Math.Sin(endLatitude) -
                Math.Sin(startLatitude) * Math.Cos(endLatitude) * Math.Cos(longitudeDelta);

        return (ToDegrees(Math.Atan2(y, x)) + 360) % 360;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
    private static double ToDegrees(double radians) => radians * 180 / Math.PI;
}
