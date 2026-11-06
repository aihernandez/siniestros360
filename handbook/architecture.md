# Architecture and engineering decisions

Siniestros360 serves one insurer. Nine services own their data and communicate through integration messages, with YARP as the client entry point. Aspire orchestrates local processes, PostgreSQL/PostGIS and the three frontends. The existing development broker is Azure Service Bus; it is an explicit external prerequisite, not an automatically free local component.

## Implemented boundaries

Claims is the lightweight CQRS pilot: command/query handlers share a database and use EF Core directly. Other services retain their own patterns. Dispatch owns a persisted assignment saga, reservations and compensation. Operations consumes events into local read models and sends authorized SignalR updates. Policy validation is asynchronous so coverage does not delay assistance. Location orders GPS updates by capture time and detects stale positions.

HTTP idempotency, business changes and outgoing messages commit in a local transaction. MassTransit inbox/outbox handles delivery and redelivery. Strong consistency ends at the service database; cross-service state is eventually consistent. No cross-service table queries, distributed database transaction or event-sourced aggregate is claimed.

## Azure reference scenario

Terraform authors private AKS/network/data stamps, WAF/TLS, Traffic Manager DNS, identity registrations, secrets and workloads. Dev/QA each configure Mexico Central; prod configures Central US, France Central, Mexico Central and Chile Central. Nine logical service databases share one PostgreSQL server per stamp. The architecture is not evidence of Azure provisioning or measured demand.

Traffic Manager selects a DNS endpoint; clients send HTTPS directly to a regional Application Gateway. Home-region ownership/fencing must be implemented before independent stamps can safely host a user's claim lifecycle. PostgreSQL/Blob local HA is not cross-region recovery. Entra registrations do not migrate the application's current HMAC JWT login. Backend replicas deliberately remain one while SignalR/session/seed/rolling-migration behavior is addressed.

AKS supplies Kubernetes control at a significant operational/cost burden. Four regions are a hypothetical design exercise; the demonstrated business workflow remains local and single-insurer. See [limitations](known-limitations.md), [Terraform](../infrastructure/terraform/README.md) and the [Azure relationship diagram](../assets/architecture/20-azure-components.png).

## Decision register

The accepted decisions are published under [adr/](adr/). They explain the gateway/discovery, Service Bus, Dispatch saga, single-insurer scope, transactional idempotency, MassTransit, authorization, Claims CQRS and target AKS choices. Scenario adoption does not certify production readiness.
