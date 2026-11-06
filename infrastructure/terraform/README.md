# Siniestros360 Azure infrastructure

Terraform implementation of the approved four-region AKS **reference scenario**. Authored infrastructure is separate from the currently running Aspire application. No resources have been deployed by this delivery. The [coverage manifest](coverage.json) maps diagram concepts to code and records the application/recovery gaps.

[Dev, QA and prod profiles](environments/README.md) are now supplied with environment-specific networks, registration prefixes, telemetry labels and separate state/backend examples. Dev/QA each use Mexico Central; prod retains the four-region scenario. These are created source configurations, not deployed Azure environments.

## Deployment hierarchy

| Order | Root | State boundary | Responsibility |
|---|---|---|---|
| 1 | `bootstrap/` | One protected bootstrap state | Azure AD authenticated state storage, versioning, firewall and operator access |
| 2 | `platform/` | One global platform state | Private Premium ACR and four independent regional stamps |
| 3, optional | `tenant/` | One customer tenant state | Create an External ID customer tenant when none exists |
| 4 | `identity/` | One identity registration state | Workforce/customer API, SPA and optional native public-client registrations |
| 5 | `regional-data/` | **One state per region** | Nine database roles/grants, PostGIS, private Blob container, application configuration and TLS in Key Vault |
| 6 | `workloads/` | **One state per region** | EF migration jobs, nine services, YARP, three SPAs, private ingress, telemetry and network policies |
| 7 | `edge/` | One global edge state | Four Application Gateways/WAFs, DNS and Geographic → Priority Traffic Manager profiles |

Reusable modules are `modules/regional`, `modules/identity` and `modules/edge`. The default regional stamps are Central US, France Central, Mexico Central and Chile Central. Each stamp has its own VNet, private AKS, PostgreSQL server with nine logical databases, Premium Service Bus, ZRS Blob Storage, Key Vault and Azure Monitor resources. PostgreSQL HA defaults to ZoneRedundant except Mexico Central, which uses SameZone; **these are proposed settings requiring regional/subscription checks**.

```text
Global DNS / Traffic Manager (DNS only)
  ├─ Central US      → Application Gateway WAF → private ILB → AKS
  ├─ France Central → Application Gateway WAF → private ILB → AKS
  ├─ Mexico Central → Application Gateway WAF → private ILB → AKS
  └─ Chile Central  → Application Gateway WAF → private ILB → AKS
       AKS: private ingress → three SPAs / YARP → nine service Deployments
       Private dependencies: PostgreSQL / Service Bus / Blob / Key Vault
```

Traffic Manager does not proxy HTTP, establish claim ownership or replicate data. North America, Europe, Mexico and South America use `GEO-NA`, `GEO-EU`, `MX` and `GEO-SA`; Mexico is a more-specific mapping. Unmapped territories have no endpoint unless `world_fallback_region` is explicitly selected. Optional recovery FQDNs are accepted only after recovery readiness is acknowledged; they do not create or promote replicas.

## Prerequisites and inputs

- Terraform **1.17.1**, Azure CLI, Python 3.12+, and Docker with a running Linux engine for image builds. `kubelogin` must be on the private deployment runner's PATH for the Kubernetes provider. Provider versions and hashes are committed in each root's `.terraform.lock.hcl`; do not run `init -upgrade` as routine deployment.
- An Azure subscription and deployment principal with resource creation plus role-assignment permissions; existing Entra AKS administrator group object IDs. Register `Microsoft.Network`, `Microsoft.ContainerService`, `Microsoft.ContainerRegistry`, `Microsoft.DBforPostgreSQL`, `Microsoft.ServiceBus`, `Microsoft.Storage`, `Microsoft.KeyVault`, `Microsoft.OperationalInsights`, `Microsoft.Insights`, `Microsoft.ManagedIdentity` and `Microsoft.PolicyInsights` before applying. The optional tenant root also requires `Microsoft.AzureActiveDirectory`. Providers deliberately do not register subscription services automatically.
- Confirm regional service/SKU/availability-zone support, AKS version and VM quotas in the actual subscription. The proposed baseline starts **six D4ds_v5 nodes per region**, plus HA PostgreSQL, Premium Service Bus, Premium ACR and WAF_v2. It has no validated price or load-based capacity estimate. The node pools autoscale; application replicas deliberately do not yet claim HA.
- A delegated child DNS zone you control and a trusted PFX certificate/private key covering `api`, `insured`, `adjuster` and `tower` under that zone. Supply PFX/password through protected `TF_VAR_tls_pfx_base64` / `TF_VAR_tls_pfx_password`, not tracked files or command history. Certificate values also enter encrypted Terraform state. Certificate issuance and parent-zone delegation remain operator actions.
- Regional data and workload stages, builds/pushes to private ACR, and Kubernetes plans require a runner with network/DNS access **inside each stamp**. A `runner` subnet with NAT is provided; runner VMs, VPN/peering and CI service registrations are not created. Install/use your own trusted self-hosted runner there. The regional PostgreSQL, AKS, Blob and Key Vault endpoints are intentionally unreachable from an ordinary public hosted runner. Separate regional DNS zones must not be linked indiscriminately to a shared VNet with overlapping namespaces.
- The Azure backend is restricted to explicit operator CIDRs and runner subnets. After platform creation, add each `runner` subnet ARM ID to bootstrap `allowed_subnet_ids` before private stages. Those subnets have `Microsoft.Storage.Global` service endpoints for cross-region backend access; NAT IP rules alone cannot authorize same-region Storage traffic. Add subnet IDs only after platform creation. See [Storage network rules](https://learn.microsoft.com/en-us/azure/storage/common/storage-network-security). Do not solve connectivity by opening `/0`.
- For identity registration, authenticate principals with application-registration/admin-consent rights in **both** tenants; configure AzureAD provider authentication appropriately. Creating a customer tenant does not bootstrap its administrator/consent. Verify any custom identity domain, configure customer sign-up/sign-in user flows, MFA/Conditional Access, role assignments, account linking and branding in the appropriate tenant. These are identity-policy decisions, not inferred from an application hostname. Native callbacks are optional and must use real package/signature-specific MSAL URIs or verified app links. No client secrets are generated.

## Apply order

Every root includes `terraform.tfvars.json.example`; roots using remote state include `backend.tfbackend.example`. Copy them to ignored local files, replace the documented sample IDs/domains and use a distinct backend key for every state. The examples contain placeholders and are not deployable credentials. Keep the platform and identity states restricted to trusted deployment operators: a backend reader can access **all** sensitive values, regardless of output redaction.

1. Initialize, review a real plan and apply `bootstrap` from an allowed IP. Protect its initial local state. After the backend exists, add `backend "azurerm" {}` to the bootstrap `terraform` block in your deployment checkout and run `terraform init -migrate-state -backend-config=backend.tfbackend`. Keep that backend declaration in the deployment checkout after migration. Never discard or recreate bootstrap state to move it.
2. Initialize `platform` with its own backend, review the resource/cost/region plan and apply. Use its `regions` and `registry` outputs. Add runner network access to bootstrap, then apply that narrowly scoped change.
3. Create `tenant` only when required. Complete customer-tenant administrator onboarding, then apply `identity` with the correct tenant-local owners and redirect URIs. These registrations prepare a future OIDC migration; they do not change the existing authentication code.
4. On each regional private runner, apply `regional-data` using its own key. PostgreSQL roles can connect/create only in their matching database; PUBLIC database/schema rights are revoked. The administrator installs PostGIS before migrations. Each service identity can read only its own configuration secret; the edge identity can read only the TLS secret. Do not add `KeyVault:Uri` to app configuration: the current configuration provider enumerates the vault and defeats this per-secret layout.
5. Build the release images, scan them, push to private ACR using an identity with `AcrPush`, then apply `workloads` per region with the generated image map. EF migration jobs finish before backend Deployments start. Jobs are named by release ID; use a new immutable release ID for every migration-bearing release and inspect failures before retrying. Existing migration jobs are managed by Terraform; no automatic database rollback is provided. An incompatible schema rollout requires a planned expand/contract migration and a reviewed backup/restore strategy.
6. Apply `edge` **with publication disabled** after private workloads and TLS configuration exist. Delegate the returned DNS name servers at your parent DNS provider. Test private paths, WAF/probe behavior, login, document upload, messaging, telemetry and failure handling in Azure. Only a subsequent reviewed plan may enable traffic after the application and ownership gaps below are resolved. Disabled publication blocks all requests at WAF and disables Traffic Manager endpoints, including direct requests to the public IP with a known Host header. The WAF block also causes public probes to fail until publication is enabled.

Typical root commands, after creating your local inputs:

```powershell
terraform -chdir=infrastructure/terraform/platform init -backend-config=backend.tfbackend
terraform -chdir=infrastructure/terraform/platform plan -out=review.tfplan
# Apply only the reviewed plan in the authorized Azure environment:
terraform -chdir=infrastructure/terraform/platform apply review.tfplan
```

Never apply all four `regional-data`/`workloads` instances against the same backend key. Either maintain four deployment working directories or reinitialize the backend deliberately with `-reconfigure` and the corresponding region's key before every plan. A region variable alone does not select a separate state.

## Container release contract

The infrastructure supplies three build recipes under `../containers/` and a release planner under `../scripts/`. The recipes follow existing .NET Dockerfiles, add EF migration bundles and build the three locked npm projects into non-root nginx images. Runtime configuration arrives from Key Vault CSI, not Docker build arguments. All images target Linux AMD64.

```powershell
python infrastructure/scripts/build_images.py --registry YOURACR.azurecr.io --release r2026-11-06-1 `
  --sdk YOUR_PINNED_DOTNET10_SDK_IMAGE --runtime YOUR_PINNED_ASPNET10_IMAGE `
  --node YOUR_PINNED_NODE22_IMAGE --nginx YOUR_PINNED_NGINX_UNPRIVILEGED_IMAGE `
  --output release.auto.tfvars.json
```

The default only prints the **22-image** build plan and writes a non-secret image map: ten backends, nine migration bundles and three SPAs. Add `--build` to build; `--build --push` additionally publishes using your existing Docker/ACR authentication. Use actual versioned image names or digests, with a Node release satisfying the app's Vite requirements and `nginxinc/nginx-unprivileged`-compatible port 8080/UID 101 behavior. Supply immutable tags or digests for the proxy and an OpenTelemetry contrib collector containing the `azuremonitor` exporter separately in workload inputs. Release tags must be immutable in your registry; a version string alone does not enforce registry immutability. EF tooling is pinned to the repository's EF Core 10.0.11 version.

`--group service_images|migration_images|frontend_images` limits both the commands and output map to that group, for parallel CI. Such output is a partial release input, not evidence that the other groups were built. Private `.env` and `.env.*` files are excluded from Docker contexts so local Vite overrides cannot enter a release image.

Frontends use same-origin `/api` and `/hubs`. Private nginx routes their hostnames to the corresponding SPA and those two paths to YARP. Kubernetes backend Services retain the existing `*-service` names on port 80, targeting container port 8080; no runtime route rewrite is needed. TLS terminates at Application Gateway; the private App Gateway → ILB → pod hop uses HTTP. End-to-end TLS/service mesh is not implemented.

## Honest application and recovery boundaries

- **Publication is closed by default.** Both `application_readiness_confirmed` and `ownership_routing_confirmed` must be true before `enable_public_traffic=true` is accepted. These are explicit operator assertions, not automated proof.
- IdentityService still issues HMAC JWTs. Demo seeding is now opt-in, password-configured and Development-only; previously persisted demo accounts still need explicit cleanup. Clients store tokens in localStorage. Infrastructure preserves a private compatibility configuration; Entra registrations do not migrate auth, remove demo accounts or harden sessions. Resolve these code gaps before acknowledging application readiness.
- Backends and YARP use **one replica and Recreate**, reflecting current SignalR, seed and in-memory rate-limit/session constraints. Kubernetes node autoscaling does not fix application replication semantics. Design SignalR backplane/sticky-session behavior, distributed limits and rolling migrations before adding application HPA/PDB/replicas. SPAs and private ingress use two replicas; collector uses one. No workload is claimed to meet a production availability SLO.
- Four independent databases are not a coherent four-way claim database. Home-region routing, writer fencing, global claim/account identity and filtered cross-region projection events remain application work. Geographic DNS alone may send an existing user's next session to the wrong independent stamp.
- PostgreSQL uses local HA/backups; Blob uses local ZRS/versioning. No cross-region PostgreSQL/Blob recovery set or Service Bus Geo-Replication is created. Recovery destinations, RTO/RPO, residency/retention and failback procedures require explicit decisions and drills before adding backup endpoints. A single-region outage currently loses that stamp's availability.
- The Documents REST adapter still requires a container SAS. Shared storage keys remain enabled only for that compatibility path. SAS lifetime is capped at 30 days, with an explicit operator-selected start/expiry. Rotate it before expiry and reapply `regional-data` then `workloads`; the config-version annotation restarts pods. CSI `subPath` mounts are not live secret rotation. Migrate the adapter to SDK/workload identity before disabling shared keys. Coordinate database/JWT credential rotation similarly; no automatic dual-key rollout is implemented.
- Current MassTransit creates queue/topic topology at startup, so its eight service identities have Service Bus Data Owner. Move topology provisioning to a controlled release step before reducing those roles to sender/receiver. Namespace resources are Terraform-managed; application-created topics/queues are not imported into state.
- Network policies restrict ingress and required ports; allowed external TLS/AMQP egress is not an FQDN firewall. NAT handles outbound SNAT, not DDoS protection or traffic inspection. A separate Azure Firewall/DDoS plan, private monitoring ingestion, managed Prometheus, service mesh and disaster-recovery automation have not been silently added.

## Verification and operations

```powershell
python infrastructure/scripts/check_terraform.py --report terraform-validation.json
# Focus a later change without rerunning unrelated roots:
python infrastructure/scripts/check_terraform.py --root modules/edge --root edge
```

The check runs `fmt -check`, backend-disabled initialization, provider-schema validation, the release-planner self-checks and **mocked plan-only Terraform tests**. It does not authenticate to Azure, create a backend, run a real cloud plan, build images or execute application regressions. The GitHub workflow runs the same checks without Azure credentials. Mocking checks composition/guards; it cannot validate subscription availability, resource API behavior, private DNS, RBAC propagation or real certificates. See [validation evidence](VALIDATION.md).

Keep reviewed plans protected like state because they may contain secrets. State storage, service databases, document storage, Key Vault secrets/certificates and the optional customer tenant use `prevent_destroy`. This is a Terraform guard, not an Azure resource lock or a backup; removing the resource block also removes its guard. Retain state and obtain a reviewed data-retention/restore plan before teardown. App Gateway managed identity/KV access, private DNS and role assignments may require Azure propagation before a dependent deployment succeeds; diagnose actual failures instead of repeatedly applying unrelated roots.

Official references: [Terraform provider mocking](https://developer.hashicorp.com/terraform/language/tests/mocking), [AzureRM provider](https://registry.terraform.io/providers/hashicorp/azurerm/4.81.0/docs), [Traffic Manager geographic codes](https://learn.microsoft.com/en-us/azure/traffic-manager/traffic-manager-geographic-regions), [External ID tenant resource](https://learn.microsoft.com/en-us/azure/templates/microsoft.azureactivedirectory/ciamdirectories), [Azure Monitor collector exporter](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/exporter/azuremonitorexporter).
