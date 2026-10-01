namespace Siniestros360.LocationService.Application;

public sealed record LocationUpdate(decimal Latitude, decimal Longitude, double? SpeedKmh, double? Heading, long Sequence, DateTimeOffset CapturedAt);
public sealed record BatchLocationItem(Guid AdjusterId, decimal Latitude, decimal Longitude, double? SpeedKmh, double? Heading, long Sequence, DateTimeOffset CapturedAt);
public sealed record BatchLocationRequest(IReadOnlyList<BatchLocationItem> Locations);
public sealed record LocationResponse(Guid AdjusterId, decimal Latitude, decimal Longitude, double? SpeedKmh, double? Heading, long Sequence, DateTimeOffset CapturedAt);
