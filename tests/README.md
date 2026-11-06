# Siniestros360 tests

Run from the repository root after restoring the solution. Select tests by the changed behavior; do not repeat the full suite for documentation-only changes.

| Project | Demonstrates | Command |
|---|---|---|
| Unit | Domain/use cases, reservations and saga | `dotnet test tests/Siniestros360.Tests.Unit` |
| Contracts | Shared message compatibility | `dotnet test tests/Siniestros360.Tests.Contracts` |
| Architecture | Namespace/layer dependency boundaries | `dotnet test tests/Siniestros360.Tests.Architecture` |
| Integration | APIs/gateway, real PostgreSQL, transactions, outbox and saga | `dotnet test tests/Siniestros360.Tests.Integration` |
| LocationSimulator | Simulator API and position generation | `dotnet test tests/Siniestros360.LocationSimulator.Tests` |

Integration requires Docker Desktop. ApiVersioningTests cover /api/v1 in Integration and Simulator; EndpointRegistrationTests cover endpoint discovery. The CQRS migration architecture checks currently identify Claims only.

Playwright requires running AppHost, configured Service Bus and an available browser:

```powershell
npm ci --prefix tests/e2e
$env:PW_CHANNEL = "chrome"
npm test --prefix tests/e2e
```

CI builds Release, runs five .NET test projects, checks formatting and builds three apps. The e2e job uses SERVICEBUS_CONNECTION_STRING when configured. [Coverage and historical evidence](../handbook/validation.md) are separate from current run results.
