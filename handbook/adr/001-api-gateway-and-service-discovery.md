# ADR-001: API gateway and service discovery

## Status

Accepted. Hosting clarification on 2026-11-06: [ADR-009](009-aks-multiregion-target.md) selects AKS for the official target. The original decision left future platform DNS open.

## Context and decision

Customer, adjuster and tower apps need capabilities from several services. Embedded internal addresses would expose topology and couple app releases to service instances. Siniestros360.Gateway with YARP is their single application API entry point, also used by the local GPS simulator. It routes requests and SignalR/WebSockets, validates JWT, applies rate limits/CORS and propagates correlation. Business rules and resource authorization are checked again inside each service. Services do not coordinate through the gateway.

Aspire supplies discovery locally; Kubernetes Services/DNS supply discovery in the selected AKS target. Consul and Eureka are unnecessary while platform discovery meets the requirement.

## Consequences and alternatives

One base URL lets topology change without mobile releases, keeps business services private and centralizes edge policy. Costs include an additional network hop, gateway scaling/monitoring and shared routing mistakes. Direct app-to-service addresses would increase coupling; an extra registry would duplicate platform features; using the gateway for internal orchestration would mix public traffic and business coordination.
