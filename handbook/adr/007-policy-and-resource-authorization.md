# ADR-007: Named policies and resource authorization

Status: accepted. Date: 2026-10-05.

## Context and decision

Approximately twenty endpoints embedded role lists, while claim participation rules were duplicated in Claims, Documents, Operations, SignalR, Adjusters and Location. Missing RequireAuthorization could accidentally expose a route.

Require authentication by default in services and gateway. YARP declares explicit route authorization. Use named ControlTower, FieldOperations, Adjuster and ClaimReporters policies. Standard ASP.NET Core AuthorizationHandler implementations enforce ClaimParticipant over ClaimParticipants and AdjusterSelf over AdjusterResource in ServiceDefaults. Each service builds the resource from its own model; no cross-service database access is allowed.

## Alternatives and consequences

A shared static helper would sit outside policy composition and leave manual rejection handling at each endpoint. Per-service handlers would duplicate one shared rule. Gateway-only authorization cannot assess record ownership. Central policies have unit coverage, new endpoints are private by default, and explicit AllowAnonymous makes public surfaces visible. Preserve 404 for another user's claim and 403 for acting as another adjuster.
