namespace Siniestros360.LocationSimulator.Models;

public sealed record AdjusterLocationUpdate(
    string AdjusterId,
    string DisplayName,
    string Status,
    string RouteName,
    double Latitude,
    double Longitude,
    double AccuracyMeters,
    double SpeedMps,
    double Heading,
    DateTimeOffset CapturedAt,
    long Sequence);

public sealed record LocationBatch(
    Guid BatchId,
    string Source,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<AdjusterLocationUpdate> Locations);
