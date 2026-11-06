# Development, QA and production profiles

These three environments are **created as Terraform source configurations**, not provisioned Azure deployments. The profiles reuse the existing seven roots and three modules; they do not fork the application or introduce a second infrastructure implementation.

| Profile | Purpose | Regions | VNet ranges | State account example |
|---|---|---|---|---|
| [dev](dev/README.md) | Cloud integration/developer verification with isolated data | Mexico Central | `10.50.0.0/16` | `s360devstate000000` |
| [qa](qa/README.md) | Controlled candidate-release acceptance and migration checks | Mexico Central | `10.60.0.0/16` | `s360qastate000000` |
| [prod](prod/README.md) | Approved four-region production reference scenario | Central US, France Central, Mexico Central, Chile Central | `10.40.0.0/16` through `10.43.0.0/16` | `s360prodstate000000` |

`profile.json` in each directory lists every root/region, input example, backend example and unique state key. There are seven stage entries in dev/QA and thirteen in prod because regional data and workloads each need four separate states. The optional customer-tenant stage is included but may be skipped when an appropriate External ID tenant already exists.

## Isolation and required replacements

Each environment has distinct state-account examples and backend keys, resource names, VNets, registration prefixes, DNS host examples and application data. The namespace remains `siniestros360` in separate clusters so it matches workload federation. Do not connect dev/QA applications to production databases, secrets, customer tenants or real insurer integrations. Use independent operator groups/principals and preferably separate subscriptions; the sample GUIDs do not establish those permissions for you.

All files ending `.example` are non-secret templates. Their IDs are synthetic, domains use `example.com`, SAS dates are illustrative, and image names are placeholders. Copy the required templates to ignored operator files and replace subscription/tenant/operator/group IDs, globally unique storage/resource names, controlled domains, verified app redirects, immutable images and fresh SAS dates. Supply PFX/password through a protected credential mechanism. Never put production data or credentials into the versioned examples. State remains sensitive even when outputs are redacted.

The node pools, HA PostgreSQL and Premium dependencies keep the authored reference sizing. A single-region dev/QA profile is **not** a low-cost SKU preset. Confirm quota, regional availability and budget before a real plan. `ASPNETCORE_ENVIRONMENT` remains `Production` for cloud workloads; `deployment.environment=dev|qa|prod` separates telemetry without enabling development-only HTTP behavior.

## Initialize and review a stage

Start with the [main deployment order](../README.md#apply-order). Bootstrap comes first, then platform, optional tenant, identity, regional data, images/workloads and edge. Private data/workload plans require a runner with the relevant stamp's DNS/network access. Add its runner subnet to the matching environment's state firewall after platform creation.

**Bootstrap exception:** before its account exists, bootstrap uses local state. Copy the bootstrap root's `.tf` files and `.terraform.lock.hcl` to a protected, ignored working directory dedicated to that environment, for example `infrastructure/.local/dev/bootstrap/`. Place that profile's edited bootstrap inputs there. Initialize/review/apply only in that copy. Do not run initial dev, QA and prod bootstrap operations from the same source directory with default local state. Migrate that environment's local state to its new backend as described in the main guide; preserve both the migration and backend declaration in its deployment copy.

For roots already using remote state, run from the repository root. This example prepares **dev platform inputs**, not cloud resources:

```powershell
$profileRoot = Join-Path (Get-Location) 'infrastructure/terraform/environments/dev'
$inputRoot = Join-Path (Get-Location) 'infrastructure/.local/dev/platform'
New-Item -ItemType Directory -Force -Path $inputRoot | Out-Null
Copy-Item -LiteralPath "$profileRoot/platform.tfvars.json.example" -Destination "$inputRoot/terraform.tfvars.json"
Copy-Item -LiteralPath "$profileRoot/backends/platform.tfbackend.example" -Destination "$inputRoot/backend.tfbackend"
# Edit both copied files: real IDs, names and matching state-account settings are required.
```

After completing the inputs and creating the backend, isolate cached backend/provider metadata per environment and stage, then initialize and review:

```powershell
$env:TF_DATA_DIR = Join-Path $inputRoot '.terraform'
terraform -chdir=infrastructure/terraform/platform init -reconfigure -backend-config="$inputRoot/backend.tfbackend"
terraform -chdir=infrastructure/terraform/platform plan -var-file="$inputRoot/terraform.tfvars.json" -out="$inputRoot/review.tfplan"
# Any apply is a separate authorized operation against that reviewed plan.
```

For a regional root, select both that environment **and** region: e.g. `regional-data/mexicocentral.tfvars.json.example` with `backends/regional-data/mexicocentral.tfbackend.example`. Use a corresponding cache directory such as `infrastructure/.local/dev/regional-data/mexicocentral/.terraform`. Do not reuse another region's backend or assume changing a variable selects a new state. Terraform workspaces are not used by these profiles.

All remote-state references in a profile point to its own platform/data keys. If you change the sample state account, update **every backend and `state` object in that environment**. Certificate coverage, DNS base domain and identity redirects must agree across identity, regional data, workloads and edge. Keep production customer tenants isolated from non-production test tenants; tenant administrator onboarding, user flows and domain verification remain prerequisites.

## Promotion and acceptance

| Stage | Required evidence before moving on |
|---|---|
| Dev | Static checks pass; image build/scan succeeds; private dependencies resolve; migrations finish; auth/report/assignment/evidence/realtime smoke flows work with synthetic data. |
| QA | Deploy the same immutable release images; validate upgrade migrations and key application flows against isolated data; verify roles, TLS/WAF, telemetry/alerts, cleanup and rollback/restore preparation. |
| Prod | Review subscription/cost/capacity and residency decisions; supply real DNS/TLS; resolve custom JWT/demo seeding/session issues and Entra migration; implement home-region routing/fencing; verify recovery and application replication semantics. |

Promotion is an operator workflow, not a newly deployed CI/CD pipeline. The existing GitHub Terraform job validates source without Azure credentials. Transfer approved image manifests/layers from the QA ACR to the production ACR using authorized private connectivity, verify the destination digest, and use the production registry address with environment-specific secrets and registration IDs. Production kubelets receive `AcrPull` for their own registry; do not silently point them at the QA registry. Do not rebuild an approved artifact solely because the environment changes.

Public ingress remains blocked in **all** profiles: `enable_public_traffic=false`, `application_readiness_confirmed=false`, `ownership_routing_confirmed=false`. Do not set these booleans merely to bypass the gates. Single-region dev/QA still need safe auth/demo data handling before any Internet exposure; production also needs coherent claim ownership/recovery across its four independent stamps. No automatic cross-region data recovery, safe multi-replica SignalR or Entra client migration is provided by these profiles.

## Validation scope

Profile checks verify disjoint network/state/name boundaries, matching remote-state keys, complete stage/region input inventory and closed publication defaults. Mocked platform plans load the actual dev/QA/prod region maps. Existing module/workload evidence is reused except where environment labels or registration inputs changed. No Azure backend, deployment, price quote, native application build or production acceptance is established by those checks.
