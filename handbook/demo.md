# Guided demonstration

This walkthrough follows a synthetic motor claim across the three applications. The [video](../assets/demo/siniestros360-walkthrough.webm) is a narrated-by-captions slideshow of **historical application captures**, not a fresh recording, live Azure deployment or new test result. It is suitable for a quick repository tour; reproduce the current flow using [local demo setup](development.md).

| Step | Screen / observed historical behavior | Engineering concept |
|---|---|---|
| Report | Customer supplies incident/location details and receives a reference | Persist immediately; coverage validation is asynchronous |
| Assign | Tower sees the claim and available/assigned units | Dispatch saga, local projections and reservation ownership |
| Track | Customer sees assignment and coverage status | Eventual consistency and authorized SignalR updates |
| Service | Adjuster records arrival and uploads evidence | Command authorization, idempotency, metadata/object separation |
| Complete | Adjuster/customer see completion and tower availability updates | Terminal events, reservation release and read-model convergence |

## Reproduce and explain

1. Start the explicitly enabled local demo with an isolated development broker and disposable database data. Open the tower, customer and adjuster apps; enter the configured local password.
2. Grant or simulate customer GPS. Report a synthetic incident and explain why assistance starts before coverage finishes.
3. Follow the claim in the tower and adjuster app. Explain the reservation and the persisted saga's retry/compensation boundaries.
4. Register arrival, add synthetic evidence, start and complete service. Observe the customer/tower converge on the result.
5. Explain a replayed intent key, outbox/inbox delivery and why a service never reads another service's tables. Use existing tests/traces for failure/rollback claims instead of pretending a screenshot proves them.
6. Show [Azure relationships](../assets/architecture/20-azure-components.png), [ADRs](adr/) and [limitations](known-limitations.md). Explain the operational cost and unimplemented ownership/recovery work behind the four-region scenario.

Map tiles retain their source attribution. No real insurer, private customer information, access keys or working demo password is included in this walkthrough. The code is .NET/React; the mobile apps use Capacitor Android.
