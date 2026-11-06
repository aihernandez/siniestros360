# ADR-004: Single insurer

## Status

Accepted by the owner on 2026-09-30. The multiregion scenario does not reverse this decision.

## Context and decision

The original multi-company design carried an insurer identifier through data, events, tokens and SignalR groups. The product serves one insurer; the unused identifier added complexity across approximately 50 files. Remove it from entities, indexes, contracts, JWT, idempotency, rate limits and groups. Authorization depends on roles and record ownership.

## Consequences and alternatives

Simpler models and fewer composite indexes; the change preceded migrations, so no existing data needed migration. Serving another insurer would require reintroducing isolation with schema/contract migration. A hidden fixed identifier would be dead code; retaining full multitenancy would add unneeded scope.
