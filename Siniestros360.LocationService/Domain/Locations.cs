using NetTopologySuite.Geometries;

namespace Siniestros360.LocationService.Domain;

public sealed class LatestAdjusterLocation
{
    public Guid AdjusterId { get; set; }
    public Point Position { get; set; } = default!;
    public double? SpeedKmh { get; set; }
    public double? Heading { get; set; }
    public long Sequence { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset? LastStaleAlertAt { get; set; }
}

public sealed class LocationHistory
{
    public Guid Id { get; set; }
    public Guid AdjusterId { get; set; }
    public Point Position { get; set; } = default!;
    public double? SpeedKmh { get; set; }
    public double? Heading { get; set; }
    public long Sequence { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}
