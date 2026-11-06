# README architecture assets

Published excerpts of the official local architecture package plus an Azure-component relationship view derived from Terraform. The canonical nineteen-view design and reconstruction model remain under docs/. Regional/global/Azure views describe authored targets, not deployed infrastructure. Service/client views describe inspected code.

| View | PNG | SVG | Editable source |
|---|---|---|---|
| 02-service-landscape | [PNG](02-service-landscape.png) | [SVG](02-service-landscape.svg) | [draw.io](02-service-landscape.drawio) |
| 03-global-multiregion | [PNG](03-global-multiregion.png) | [SVG](03-global-multiregion.svg) | [draw.io](03-global-multiregion.drawio) |
| 04-regional-aks | [PNG](04-regional-aks.png) | [SVG](04-regional-aks.svg) | [draw.io](04-regional-aks.drawio) |
| 06-internal-claims | [PNG](06-internal-claims.png) | [SVG](06-internal-claims.svg) | [draw.io](06-internal-claims.drawio) |
| 19-client-applications | [PNG](19-client-applications.png) | [SVG](19-client-applications.svg) | [draw.io](19-client-applications.drawio) |
| 20-azure-components | [PNG](20-azure-components.png) | [SVG](20-azure-components.svg) | [draw.io](20-azure-components.drawio) |

The root README selects services, regional deployment, global routing, Claims CQRS, client/console internals and the additional Azure relationship view. Screenshots under assets/screenshots are copied from historical demo evidence; the product UI remains Spanish. The product has one React web console and two React/Capacitor Android apps; no React Native implementation is claimed.

Rebuild changed canonical diagrams with the local docs/reconstruction/tools/build_diagrams.py, inspect their exports, then run finalize_package.py to refresh these publication copies. Edit view 20 directly in its draw.io source and re-export PNG/SVG when its relationships change; the finalizer preserves this additional view. The full documentation stays under the existing local-only Git policy. Publish this assets directory together with the README so relative image links resolve on the Git host.

[Publication fingerprints](provenance.json)
