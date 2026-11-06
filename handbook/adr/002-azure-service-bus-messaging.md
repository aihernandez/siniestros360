# ADR-002: Azure Service Bus messaging

## Status

Superseded by [ADR-006](006-masstransit-messaging.md) on 2026-09-30. Service Bus remains the broker; the library, topology and emulator choice changed. This is historical evidence, not current configuration.

## Original context and decision

The specification required event-based integration, Azure Service Bus, local execution and removal of the previous broker. The original design used a domain topic (claims.events, dispatch.events, etc.), a subscription per consumer, Azure.Messaging.ServiceBus, outbox-only publication, PeekLock consumption and inbox deduplication. Aspire hosted the official emulator. DevelopmentHttp behind IMessageTransport was an alternative without the emulator. Cloud used Standard and DefaultAzureCredential when SAS was absent.

## Consequences and alternatives

The original design provided broker retries, duplicate detection and DLQ without exposing its SDK to domain code, and could run without Azure. Its emulator required roughly 1.5 GB of images including SQL Server, lost messages on restart, and required keeping AppHost/appsettings topology aligned. Keeping the previous broker violated the specification. Event Hubs was unsuitable as the general business broker; ILocationEventSink preserved a future GPS adapter boundary. HTTP-only integration coupled availability and lost asynchronous at-least-once delivery.
