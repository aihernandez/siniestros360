namespace Siniestros360.Contracts.Events;

// Catálogo, disponibilidad y estado operativo de los ajustadores.
// El namespace es el mismo en todas las carpetas: forma la URN de MassTransit y el nombre del topic.
public sealed record AdjusterRegistered(Guid AdjusterId, string DisplayName, DateTimeOffset RegisteredAt);
public sealed record AdjusterAvailabilityChanged(Guid AdjusterId, bool IsAvailable, DateTimeOffset ChangedAt);
public sealed record AdjusterStatusChanged(Guid AdjusterId, string PreviousStatus, string NewStatus, DateTimeOffset ChangedAt);
