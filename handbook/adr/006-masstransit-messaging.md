# ADR-006: MassTransit 8 messaging

## Status

Accepted 2026-09-30; supersedes [ADR-002](002-azure-service-bus-messaging.md). Review due December 2026. Licensing/support statements below record the assessment made on that date, not a fresh vendor verification.

## Context and decision

The previous custom inbox/outbox and transport required approximately 600 infrastructure lines and lacked saga state-machine support. The owner chose MassTransit and transactional HTTP idempotency. The historical assessment selected 8.5.11 under Apache 2.0 rather than commercial v9, with a support review before the end of 2026.

- MassTransit 8.5.11 over Azure Service Bus, using EF bus outbox, consumer outbox and inbox in each service database.
- One business queue per participant and separate GPS telemetry queues for Dispatch/Operations (added 2026-10-03 after assignment delays around 17 seconds).
- Native DLQ with MaxDeliveryCount 5. MassTransit provisions topology but does not remove obsolete subscriptions; see [topology changes](../architecture.md#implemented-boundaries).
- Common messaging schema, ReadCommitted isolation and retry handling for 40001, 40P01, 23505 and DbUpdateException.
- Assignment becomes MassTransitStateMachine; development uses a real Standard namespace rather than the emulator.

## Consequences and alternatives

Custom transport code is removed; the library provides retries, inbox, saga concurrency, test harness and telemetry. Costs include Azure-dependent development, Manage permissions for topology provisioning, library-defined envelopes/topics and process-wide SQL caching requiring a common schema in multi-service integration hosts.

The assessed emulator exposes administration on HTTP 5300 and messaging on AMQP 5672; this MassTransit configuration uses one connection string and stalls at queue administration. Keeping custom code would preserve emulator support but retain maintenance. Wolverine was an alternative, not selected. The owner preferred real Azure over local RabbitMQ/SQL substitutions. Reassess licensing/support in December 2026; no current price quote is asserted.
