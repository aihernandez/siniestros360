namespace Siniestros360.Contracts.Events;

// GPS de los ajustadores. AdjusterLocationUpdated es telemetría de alta frecuencia y se consume en colas -telemetry.
// El namespace es el mismo en todas las carpetas: forma la URN de MassTransit y el nombre del topic.
public sealed record AdjusterLocationUpdated(Guid AdjusterId, decimal Latitude, decimal Longitude, double? SpeedKmh, double? Heading, long Sequence, DateTimeOffset CapturedAt);
public sealed record AdjusterGpsStaleDetected(Guid AdjusterId, DateTimeOffset LastSeenAt, DateTimeOffset DetectedAt);
