# ADR-005: Attribute-based HTTP idempotency

## Status

Accepted by the owner 2026-09-30; amended that day so the full command shares a transaction (BUG-027 through BUG-031).

## Context and decision

Critical commands must tolerate client retries without duplicating effects. Replace manual header parsing with the requested Idempotent attribute/filter approach.

- Mark handlers with [Idempotent] and enable WithIdempotency on their route group.
- Require a GUID Idempotency-Key (400 if missing/invalid).
- Store records in each service database, shared by its replicas, without adding Redis.
- Reserve the key, execute business/outbox work and save the successful response in one local DbContext transaction. Exceptions, non-2xx results and failed commits roll everything back.
- A unique (UserId, Key) index isolates users. Concurrent twins wait on the first transaction and replay its response.
- Compare method/path/payload hashes; reuse with different content returns 409.
- Replay successful responses with Idempotent-Replayed: true. Records expire after 60 minutes and a worker purges them.
- Register external rollback actions, such as deleting an uploaded object after a failed metadata transaction.
- Apps generate one key per intent and reuse it on retries.

## Consequences and alternatives

One reusable implementation closes the race in the simpler reference pattern. The transaction stays open during the handler, Location is not replayed, and a process crash between file upload and commit can still leave an orphan pending reconciliation. Repeated endpoint parsing would be easy to omit; a distributed-cache-only approach adds Redis and does not reserve the key transactionally.
