# ADR-009: Official AKS multiregion scenario baseline

Status: **accepted for scenario documentation by the owner on 2026-11-06**. Implementation and deployment are not approved or demonstrated by this status. This refines ADR-001 hosting and replaces earlier unselected hosting assumptions; ADR-001's gateway decision remains accepted.

## Context and decision

The owner approved the Azure/microservices visual direction, four-region routing, WAF, Entra identity and CQRS-style internal presentation, then requested Architecture Reverse and English documentation.

Adopt the [official gallery](../../assets/architecture/README.md) and [ADD](../architecture.md) as the target documentation. Use one AKS stamp in Central US, France Central, Mexico Central and Chile Central. Regional ingress uses Application Gateway WAF_v2/TLS, private ingress and YARP. Keep service-owned PostgreSQL databases outside AKS, regional Service Bus/Blob/Monitor, and ACR image distribution. Traffic Manager resolves regional endpoints through Geographic and nested Priority profiles. Select workforce Entra ID and customer External ID as the target identity providers.

Retain the actual nine services and their different internal patterns. Do not convert all services to CQRS or change functional scope merely to match a diagram. Keep current HMAC identity and Aspire execution documented separately from the target.

## Alternatives and consequences

A simpler managed container host would reduce cluster operations; a single region would reduce replication/routing complexity. AKS and four regional stamps satisfy the owner's chosen learning/presentation scenario and allow explicit Kubernetes workload/network control, at higher operational cost and complexity. No measured demand or cost quote justifies production sizing yet.

## Conditions and reconsideration

Home-region routing/fencing, residency, RTO/RPO, DR destinations, regional SKU/HA support, SignalR scale-out, Entra migration and live network/certificate validation remain open. The subsequent owner request authorized authoring [Terraform](../../infrastructure/terraform/README.md); it did not authorize cloud execution. Its concrete network/hosting/sizing defaults remain proposals pending a real deployment review. Revisit the design when workload, budget, residency or recovery objectives become concrete. No production readiness is claimed.
