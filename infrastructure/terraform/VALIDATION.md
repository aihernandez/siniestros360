# Terraform validation evidence

Checked locally on **2026-11-06** with Terraform **1.17.1** on Windows AMD64. [Machine-readable command results](validation.json) record the actual checks; all recorded exit codes are zero.

| Check | Result | What it establishes |
|---|---|---|
| `terraform fmt -check -recursive` | Passed | HCL/test formatting and parsing |
| `init -backend=false` + `validate` | Passed for seven roots and three modules | Pinned providers resolve; references and provider schemas are valid |
| `terraform test` with mocked providers, `command = plan` | **17 passed, 0 failed** | Four-stamp/database inventory, private boundaries, service-secret access, WAF defaults, readiness/recovery guards, native/SPA registration validation, SAS lifetime and workload/image contracts |
| Python release planner | **2 passed** | All 22 release images map to actual projects/recipes; unsafe/unversioned inputs are rejected |

The scope is justified by new infrastructure, identity/network policies, migration/deployment configuration and a release script. Terraform/provider mocks check composition and security guards without cloud side effects. They do not require repeating unchanged .NET or browser regressions. The earlier failed mock fixtures were corrected; the final whole-package check is the receipt linked above.

## Dev / QA / prod and README follow-up

The [focused receipt](environments/validation.json) revalidates the changed platform, identity and workload roots. Five platform mocked plans pass, including plans loading each actual dev/QA/prod region map; two workload mocked plans pass, including the QA telemetry-label assertion. Identity root validation passes with environment-specific registration prefixes. The five Python checks pass: two unchanged image-planner checks plus three new checks for disjoint networks/state/registration names, complete regional inputs/closed publication, and README PNG paths/case/file structure.

The thirteen other baseline mocked plans remain applicable to unchanged roots/modules; their earlier evidence is reused. Together with the seven current focused plans, the current source is covered by **20 mocked plan cases**, without claiming that all twenty were rerun in the follow-up. No unchanged .NET/business or browser application regression was rerun for these documentation/profile changes.

All **nine README PNGs** were decoded/verified from their actual files. The additional Azure relationship PNG was visually inspected after correcting a text overflow and a crossed heading; its PNG/SVG/editable draw.io files are under `assets/architecture/20-azure-components.*`. Standard Markdown replaces screenshot HTML tables and the collapsed global diagram. Local image paths are case-correct for Linux Git hosting. Publish `assets/` together with the README; a local file check does not upload repository content to a remote host.

The profiles contain 27 stage entries, 27 input examples and 27 corresponding backend examples across dev/QA/prod. Inputs contain synthetic IDs and sample domains rather than deployment credentials. The [environment guide](environments/README.md) records bootstrap local-state isolation, backend/cache selection and promotion requirements. Source profiles are created; Azure environments are not provisioned.

## Public release preparation

[Publication authoring receipt](publication-validation.json) records the repeated schema/mocked checks for the release candidate. Media validation now belongs to the public repository workflow rather than the Terraform profile tests. Infrastructure Python checks cover the complete release inventory, invalid inputs, partial-group manifest behavior and environment isolation. The added CLI regression checks all three partial image groups and confirms that unbuilt groups are absent from the emitted map. Docker contexts exclude private `.env.*` files. Actual Linux image/integration checks are linked from the GitHub release; these local receipts do not claim those remote results.

Provider pins: AzureRM 4.81.0, AzureAD 3.10.0, AzAPI 2.13.0, Kubernetes 2.38.0, Random 3.9.1 and PostgreSQL 1.27.0. The Terraform executable's download SHA256 was checked against HashiCorp's release sums. Provider packages were initially downloaded with registry signature verification, then reused through a local filesystem mirror when registry resolution stalled. Dependency locks retain the original registry checksums. CI performs normal registry initialization.

No Azure credentials, resource-provider registration, backend creation, real Azure plan/apply/destroy, image push, price quote, tenant/domain setup, certificate issuance, recovery drill or application regression was executed. Docker's Linux engine was unavailable, so the container recipes and EF bundles were **not built or run**. The release planner inventory was checked independently of Docker. The new GitHub workflow is authored; a hosted GitHub execution is not claimed.

Before deployment, fill real IDs/domains/certificates, confirm SKUs/zones/quotas, prepare private runners and review a real Azure plan. Before publication, resolve custom-JWT/session risks and previously persisted demo accounts, Entra migration, regional ownership/fencing, SignalR replication and recovery/residency objectives. [Deployment instructions](README.md) describe the ordering and per-region state requirements. No production-readiness claim follows from local validation.
