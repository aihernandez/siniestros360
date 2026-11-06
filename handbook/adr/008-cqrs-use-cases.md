# ADR-008: CQRS use cases without a mediator

Status: accepted. Date: 2026-10-05.

## Context

Endpoints mixed HTTP, authorization, validation, entity creation, persistence, events, telemetry and mapping. Operations handled twenty events in one consumer. The owner selected the Milan Jovanović Clean Architecture template as a reference for endpoints, use cases and extensions.

## Decision

- Application-level CQRS without separate per-service read/write stores. Operations already provides the system-level operational read side.
- ICommand/IQuery handlers return Result; errors have stable types/catalogs. IEndpoint discovery registers one endpoint per use case. Match/CustomResults.Problem translate results. Scrutor decorates handlers with validation/instrumentation; FluentValidation and namespace architecture checks support the boundaries.
- One executable per service, with Domain, Application/resource/use-case, Infrastructure and Endpoints/resource folders. SharedKernel contains Result/Error/CQRS/decorators/AddApplication; ServiceDefaults contains HTTP conventions and middleware.
- Use Forbidden errors, OpenTelemetry/log instrumentation and IUserContext resource policies. MassTransit publish filtering propagates correlation.
- Preserve the transactional idempotency implementation in [ADR-005](005-attribute-based-idempotency.md), at the HTTP endpoint boundary rather than a handler decorator.
- Add Npgsql traces, Development-only or opt-in startup migrations, correlation log scopes, centralized 409 concurrency handling, Bearer OpenAPI and a gateway fallback limiter.

## Alternatives and consequences

Direct handler injection makes a mediator unnecessary. Splitting every service into four library projects would add roughly 36 projects. Separate read stores per service have no demonstrated load justification and would delay read-after-write visibility. The reference template's Serilog/Seq, Swashbuckle, HybridCache and DomainEventsDispatcher are not imported: native OpenAPI, Aspire/OTel and MassTransit outbox already cover the selected needs.

Add a use-case folder and endpoint class without changing Program.cs. Handlers can be exercised without HTTP; production migrations belong to deployment. Migration is incremental: MigratedToUseCases currently lists only Claims. This is pragmatic layering; handlers directly reference the service DbContext, not a claim of strict dependency inversion. [Internal views](../../README.md#internal-architecture-of-every-service) reflect this distinction.
