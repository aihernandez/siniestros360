# Local demonstration

Requirements: .NET 10 SDK, Node.js 22 satisfying the Vite package engine requirement (Node 24 also works), Docker with a working Linux engine, and an isolated Azure Service Bus Standard namespace or higher with topology-management permission. The current MassTransit configuration does not support the emulator's split admin/messaging ports. Azure resources can incur charges; no deployment is performed by cloning/building the repository.

From the repository root, configure the AppHost using private user secrets. On PowerShell 7, choose a unique local password and enter the broker connection string without echoing either value:

```powershell
$demoPassword = Read-Host -MaskInput 'Unique local demo password (12+ characters, upper/lower/digit/symbol)'
$broker = Read-Host -MaskInput 'Isolated development Service Bus connection string'
$jwt = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
@{
  'Demo:Enabled' = 'true'
  'Parameters:demo-password' = $demoPassword
  'Parameters:jwt-signing-key' = $jwt
  'ConnectionStrings:servicebus' = $broker
  'Messaging:EntityPrefix' = 'local-demo-'
} | ConvertTo-Json | dotnet user-secrets set --project src/aspire/Siniestros360.AppHost
$env:SINIESTROS360_DEMO_PASSWORD = $demoPassword
Remove-Variable broker, jwt
dotnet run --project src/aspire/Siniestros360.AppHost
```

Demo mode is off by default and rejects enabled seeding outside Development. The AppHost forwards the configured password only to the local Identity/Adjusters seed and the simulator. Enter that password in each app's login form; it is not baked into JavaScript. The account addresses are synthetic fixtures: `insured.demo@demo.com`, `adjuster.demo@demo.com`, `tower.demo@demo.com`, `admin.demo@demo.com`.

| Surface | Local address |
|---|---|
| Gateway | http://localhost:5090 |
| Customer app | http://localhost:5173 |
| Adjuster app | http://localhost:5174 |
| Control tower | http://localhost:5175 |

Read the actual AppHost output for dashboard/simulator endpoints. Allow database migration and resource health to complete before login. Geolocation requires browser permission or synthetic browser coordinates in Playwright. Use synthetic policy/vehicle/GPS/document data only.

For Playwright, keep AppHost running and set `SINIESTROS360_DEMO_PASSWORD` to the same local password in the test terminal. Install the locked e2e packages with `npm ci --prefix tests/e2e`, then run `npm test --prefix tests/e2e`. Tests need a browser already installed or the documented Playwright browser installation. The optional GitHub live-broker e2e job requires a repository secret; missing credentials are reported as a skipped capability, not a passing business journey.

Stop AppHost when done to stop simulation/broker traffic. The PostgreSQL volume is persistent: changing the seed password does not erase existing accounts. For a new demo, select a new disposable data volume or remove the old demo volume only after inspecting it and confirming no needed data remains. Disabling seeding does not revoke existing accounts. This guide never automatically deletes databases or provisions a broker.
