# Validation and release evidence

The first public demonstration release is v0.1.0. Source and execution claims are separate: historical screenshots illustrate implemented journeys, while GitHub check/run URLs identify what actually passed for a reviewed commit.

## Acceptance layers

| Layer | Acceptance | Limit |
|---|---|---|
| Domain/use cases | Unit tests for transitions/reservations and explicit demo guard/seed behavior | No broker/network proof |
| Integration | Real PostgreSQL/PostGIS, migrations, API ownership, transactions, outbox and saga cases | Uses the test messaging transport, not a live Azure broker |
| Contracts/architecture | Shared message compatibility and intended dependency boundaries | Not production deployment verification |
| Frontends | Locked npm installs, TypeScript/Vite builds for all three applications | Not physical-device acceptance |
| Optional live e2e | Customer/tower/adjuster and API journey against AppHost and an isolated development Service Bus | Requires the repository's configured broker secret; missing credentials are explicitly not an executed test |
| Container recipes | Build ten backends, nine EF bundles and three SPAs on Linux runners, without pushing | Not AKS rollout/connectivity validation |
| Terraform | Formatting, provider schemas, mocked plans and profile/media checks | No Azure plan/apply, quotas, DNS/RBAC propagation, cost or recovery validation |

Run commands are in [tests/README.md](../tests/README.md) and the [Terraform guide](../infrastructure/terraform/README.md). The CI workflow runs five .NET suites and three app builds. Infrastructure CI runs its own focused Python/Terraform checks. Container CI builds the three image groups without registry credentials. The optional broker e2e capability must be read separately from general green build checks.

The local publication work discovered Docker Desktop cannot start because its internal ingest socket is inaccessible. Local Docker-dependent results must not be invented; the Linux GitHub runners provide the release integration/container evidence. A release notes entry links actual successful PR/main checks after integration. A failed required check blocks merging; a declared optional credential-dependent capability remains documented as unexecuted when unavailable.

## Evidence retained

- [Terraform baseline receipt](../infrastructure/terraform/validation.json) and [environment follow-up](../infrastructure/terraform/environments/validation.json) are dated authoring evidence, not new cloud executions.
- The [guided walkthrough](demo.md) uses original synthetic historical UI captures and labels that provenance in the video/page.
- [Known limitations](known-limitations.md) retain BUG-045 and unverified mobile/HA/recovery claims.
- GitHub Actions logs/artifacts and the release's commit/PR URLs are authoritative for the publication candidate. Do not copy private AppHost logs or Azure connection strings into a public artifact.
