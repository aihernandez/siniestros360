# Services and implementation patterns

AppHost starts nine services, YARP, PostgreSQL/PostGIS, the GPS simulator and three web apps. Each service owns its data and migrations. HTTP clients enter through YARP; MassTransit events coordinate services. The [service catalog](../handbook/architecture.md) owns responsibility/event definitions; the [official design](../handbook/architecture.md) maps this application onto the AKS target.

## Endpoints and use cases

REST routes use internal sealed IEndpoint implementations, registered through AddEndpoints and MapEndpoints on versioned groups. In the Claims CQRS pilot, each endpoint translates an HTTP request into a command/query, injects its handler and maps Result to an HTTP response. Domain/application logic stays below the HTTP boundary. Endpoints declare named authorization policies and stable operation names.

SharedKernel supplies ICommand/IQuery, handlers, Result/Error, FluentValidation and instrumentation decorators. AddApplication discovers handlers through Scrutor. No mediator is used. Claims commands and queries share ClaimsDbContext and a database; this is not event sourcing. Other services are not yet migrated to this handler structure. [ADR-008](../handbook/adr/008-cqrs-use-cases.md) records the phased decision.

IDateTimeProvider is registered in ServiceDefaults, allowing controlled clocks for domain/use-case tests. ServiceDefaults also supplies endpoint discovery, version conventions, ProblemDetails, health/OTel, correlation and named role/resource policies.

## Versions and communication

ApiVersions.V1 defines /api/v1 routes and response URLs. Development publishes versioned OpenAPI. YARP resolves local service names through Aspire service discovery. The Terraform target authors Kubernetes Services/DNS and workload resources; it has not been deployed in Azure. Services exchange business messages over Service Bus rather than calling one another through YARP.

AddReliableMessaging configures Service Bus when a connection is present, otherwise process-local transport for tests/diagnostics. Bus outbox covers request publications; consumer inbox/outbox covers received messages. Business and telemetry queues are separated; exhausted retries use the native DLQ. Dispatch's AssignmentStateMachine persists saga state, reservations and outbox together with optimistic concurrency. It begins policy validation and assignment independently; release compensates a matching reservation.

NuGet/framework versions are centralized in Directory.Packages.props/Directory.Build.props. App package files own JavaScript dependencies. [Root dependency table](../README.md#declared-dependency-versions) is the public summary.
