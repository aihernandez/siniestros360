using Siniestros360.LocationSimulator.Models;
using Siniestros360.LocationSimulator.Services;

namespace Siniestros360.LocationSimulator.Tests;

public sealed class GeoMathTests
{
    [Fact]
    public void DistanceMeters_ReturnsZero_ForSamePoint()
    {
        var point = new GeoPoint(25.6866, -100.3161);
        Assert.Equal(0, GeoMath.DistanceMeters(point, point), precision: 6);
    }

    [Fact]
    public void Interpolate_ReturnsMidpoint()
    {
        var result = GeoMath.Interpolate(new GeoPoint(25, -100), new GeoPoint(26, -101), 0.5);
        Assert.Equal(25.5, result.Latitude);
        Assert.Equal(-100.5, result.Longitude);
    }
}
