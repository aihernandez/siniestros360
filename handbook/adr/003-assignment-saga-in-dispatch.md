# ADR-003: Assignment saga inside Dispatch

## Status

Accepted 2026-09-30; updated that day for MassTransit ([ADR-006](006-masstransit-messaging.md)); extended 2026-10-04 to orchestrate policy validation independently of dispatch.

## Context and decision

Assignment spans Claims reports, Adjusters availability, Location GPS and SLA deadlines. It requires persistent state, exclusive reservations, retries and compensation.

- AssignmentStateMachine is a MassTransit state machine in DispatchService, correlated by ClaimId.
- Its EF repository uses ExistingDbContext. Saga state, reservation and outgoing events commit together in DispatchDbContext; xmin provides optimistic concurrency.
- Decisions use event-fed local availability/GPS projections, never another service database.
- Compensation releases only a reservation still owned by that claim during replacement, close or cancellation.
- Reassignment is allowed for an arrival SLA breach or tower command while Assigned, never because of arrival SLA while OnSite. Manual commands return 202 and become saga messages.
- ClaimReported publishes PolicyValidationRequested while beginning assignment. Coverage Pending/Active/Rejected/Failed/Unavailable is recorded without blocking service or releasing a reservation. Claims/Operations project the same outcomes. Older sagas retain null coverage.

## Consequences and alternatives

There is one process owner, declarative transitions, restart persistence and explicit handling of inapplicable events. Eventual projections can lag; a report racing the first GPS waits for the next update. Haversine is not road ETA. A generic WorkflowService would accumulate unrelated domain knowledge; synchronous assignment in Claims would block reporting; pure choreography would obscure reservations, retries and compensation. [Runtime/state views](../architecture.md) show the selected design.
