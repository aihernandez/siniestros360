namespace Siniestros360.Contracts.Messaging;

public sealed record IntegrationEventEnvelope<TPayload>
{
    public required Guid MessageId { get; init; }
    public required Guid CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required string EventType { get; init; }
    public required int SchemaVersion { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public long? AggregateVersion { get; init; }
    public required TPayload Payload { get; init; }
}

public sealed record MessageMetadata
{
    public required Guid MessageId { get; init; }
    public required Guid CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required string EventType { get; init; }
    public required int SchemaVersion { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public long? AggregateVersion { get; init; }
}
