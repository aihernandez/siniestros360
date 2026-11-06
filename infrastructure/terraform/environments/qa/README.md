# QA environment

Release-candidate acceptance in isolated test data. **Configuration created; Azure deployment not executed.**

[Setup, isolation and promotion](../README.md) · [Stage and state-key manifest](profile.json)

Regions: mexicocentral.

Use this directory’s .tfvars.json.example inputs with matching files under ackends/. Copy and replace sample IDs, account names, domains, images and dates before a real plan. Each regional data/workload pair has its own state key. The optional tenant stage may be skipped when a suitable isolated customer tenant exists.

Public traffic remains disabled at WAF and Traffic Manager. Identity registrations are prefixed s360-qa; workload telemetry uses deployment.environment=qa. The Kubernetes namespace remains siniestros360 in separate clusters. No production-readiness assertion or automatic data recovery is implied.
