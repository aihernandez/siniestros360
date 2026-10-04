namespace Siniestros360.LocationSimulator.Models;

public sealed record RouteDefinition(string Name, IReadOnlyList<GeoPoint> Points);
