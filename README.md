# Siniestros360

Siniestros360 es una demo técnica de atención de siniestros de autos para una sola aseguradora. El asegurado reporta un siniestro con su ubicación, el sistema asigna al ajustador disponible más cercano, la torre de control sigue el caso en tiempo real y el ajustador registra llegada, inicio y cierre. La validación de póliza es asíncrona y nunca bloquea la atención.

Está construida con .NET 10, Aspire, YARP, PostgreSQL/PostGIS, MassTransit sobre Azure Service Bus, SignalR y React/Vite con Capacitor. El [mapa de servicios y patrones](src/README.md) describe cómo se conectan estas piezas en el código.

## Arquitectura

- `Gateway`: YARP, JWT, rate limiting por ruta, CORS para apps móviles y correlación.
- `IdentityService`: usuarios, roles, JWT y refresh tokens.
- `ClaimsService`: expediente, folio, máquina de estados y bitácora.
- `AdjustersService`: catálogo, disponibilidad y estado operativo de ajustadores.
- `LocationService`: GPS con PostGIS y detección de GPS sin actualizar.
- `DispatchService`: saga de MassTransit que inicia en paralelo la validación de póliza y la asignación; registra el resultado y compensa las reservas al reasignar, cerrar o cancelar.
- `PolicyService`: validación asíncrona con timeout, retry, circuit breaker y fallback.
- `SlaService`: plazos por etapa, avisos, vencimientos y escalación.
- `DocumentsService`: metadatos y almacenamiento local o Azure Blob.
- `OperationsService`: vistas de lectura de la torre y SignalR.
- `Messaging` y `Contracts`: configuración de MassTransit, idempotencia transaccional y contratos de integración.
- `AppHost`: topología local con Aspire.

Cada servicio tiene su propia base de datos y migraciones. Ningún servicio consulta tablas de otro.

## Estructura

| Carpeta | Contenido |
|---|---|
| `src/aspire/` | `AppHost` (topología local) y `ServiceDefaults` (telemetría, health checks, resiliencia, seguridad común) |
| `src/shared/` | `Contracts` (eventos, roles y versiones de API), `SharedKernel` (CQRS y Result) y `Messaging` (MassTransit, outbox, inbox e idempotencia) |
| `src/gateway/` | `Gateway` (YARP) |
| `src/services/` | Los 9 microservicios, cada uno con `Endpoints/` (comandos y consultas HTTP), `Domain/`, `Application/` (consumidores y DTO), `Infrastructure/` y su `Dockerfile` |
| `src/tools/` | `LocationSimulator` (GPS simulado de los 5 ajustadores) |
| `tests/` | Unitarias, de contrato, de arquitectura, de integración, del simulador y `e2e/` con Playwright; ver [guía de pruebas](tests/README.md) |
| `apps/` | `insured-app`, `adjuster-app` y `control-tower`: React/Vite; las dos primeras se empaquetan con Capacitor |

La autorización se declara con políticas con nombre y por recurso de `ServiceDefaults/Security`; todo endpoint es privado salvo `AllowAnonymous`.

Las API HTTP de negocio publican `/api/v1/...` con metadatos de versión y OpenAPI por versión.

### Versiones de API

El contrato REST usa la versión mayor en la ruta: `/api/v{version}/...`. La versión publicada es **v1** en Identity (`auth`), Claims, Adjusters, Location (`locations`), Dispatch (`dispatch/claims`), Documents y Operations. El simulador local expone `/api/v1/simulator`. El gateway acepta el segmento de versión y conserva la ruta; cada servicio decide si la versión está soportada. En desarrollo, cada servicio REST y el simulador generan `/openapi/v1.json`; las respuestas versionadas informan `api-supported-versions`.

`ApiVersions.V1` en `src/shared/Siniestros360.Contracts/Common/ApiVersions.cs` es la constante de la versión mayor. Los registros de rutas y las URL de respuesta se construyen con ella; el gateway conserva una plantilla de ruta capaz de recibir otras versiones.

Los cambios compatibles permanecen en v1. Un cambio que modifica de forma incompatible rutas, campos requeridos, su significado o códigos de respuesta crea v2. Durante la migración, ambas versiones deben funcionar y tener pruebas de contrato. Antes de retirar una versión se registra y comunica la fecha de fin de soporte y la ruta de migración; una versión obsoleta se anuncia con `api-deprecated-versions`. La versión HTTP es independiente de la release Git y de los eventos de mensajería.

`/health`, `/alive`, `/gateway/info`, los hubs SignalR y la página raíz del simulador son superficies operativas fuera del contrato REST versionado. PolicyService y SlaService no exponen endpoints REST de negocio.

Cada proyecto .NET vive en una carpeta con su nombre completo (`src/services/Siniestros360.ClaimsService/`). La solución `Siniestros360.slnx` tiene las mismas carpetas. Los Dockerfiles se construyen desde la raíz: `docker build -f src/services/Siniestros360.ClaimsService/Dockerfile .`

## Requisitos

- .NET 10 SDK.
- Node.js 22.
- Docker Desktop (PostgreSQL local y pruebas de integración).
- Un namespace de Azure Service Bus tier Standard (Basic no admite topics). El emulador local no funciona con MassTransit 8, porque su API de administración usa otro puerto. El tier Standard tiene un cargo base de unos 10 USD al mes.

## Versiones de librerías

Estas son las versiones **declaradas** por el repositorio. `Directory.Packages.props` es la fuente de las versiones NuGet; los `package.json` y `package-lock.json` de cada app fijan sus dependencias JavaScript. Los prefijos `^` y `~` de npm permiten instalar versiones compatibles dentro del rango indicado.

| Componente | Versión declarada |
|---|---|
| .NET / ASP.NET Core / EF Core | 10 / 10.0.11 / 10.0.11 |
| .NET Aspire | 13.5.4 |
| PostgreSQL/PostGIS (imagen local) | 16-3.4 |
| Versionado de API (`Asp.Versioning.Http`, `OpenApi`) | 10.2.3 |
| YARP / Service Discovery | 2.3.0 / 10.8.0 |
| Resiliencia HTTP (`Microsoft.Extensions.Http.Resilience`) | 10.8.0 |
| MassTransit y transporte Azure Service Bus | 8.5.11 |
| Npgsql EF Core / PostGIS | 10.0.3 |
| Scrutor / FluentValidation | 7.0.0 / 12.1.1 |
| OpenTelemetry SDK y exportador OTLP | 1.15.3 |
| React / Vite / TypeScript | ^19.2.8 / ^8.2.2 / ~6.0.2 |
| Capacitor Android / SignalR JavaScript | ^8.5.1 / ^10.0.11 |
| xUnit / Testcontainers PostgreSQL / Playwright | 2.9.3 / 4.15.0 / ^1.63.0 |

## Ejecución local

Crea el namespace con Azure CLI. La cadena necesita permiso Manage porque MassTransit crea su topología al arrancar (un topic por tipo de evento y una cola por servicio):

```powershell
az group create -n <grupo> -l southcentralus
az servicebus namespace create -g <grupo> -n <nombre-unico> -l southcentralus --sku Standard
$cs = az servicebus namespace authorization-rule keys list -g <grupo> --namespace-name <nombre-unico> -n RootManageSharedAccessKey --query primaryConnectionString -o tsv
```

Configura una sola vez los secretos del AppHost (quedan en user-secrets, fuera del repositorio):

```powershell
dotnet user-secrets set "Parameters:jwt-signing-key" "<clave-de-al-menos-32-caracteres>" --project src/aspire/Siniestros360.AppHost
dotnet user-secrets set "ConnectionStrings:servicebus" $cs --project src/aspire/Siniestros360.AppHost
```

Levanta todo:

```powershell
aspire run
# o bien
dotnet run --project src/aspire/Siniestros360.AppHost
```

| Recurso | URL |
|---|---|
| Dashboard de Aspire | https://localhost:17025 |
| Gateway | http://localhost:5090 |
| App del asegurado | http://localhost:5173 |
| App del ajustador | http://localhost:5174 |
| Torre de control | http://localhost:5175 |

Apaga el AppHost cuando no lo uses: el simulador GPS genera alrededor de 1.3 millones de operaciones por día en Service Bus.

Usuarios demo, todos con contraseña `Demo!2026`:

- `insured.demo@demo.com` (asegurado)
- `adjuster.demo@demo.com` (Ajustador 01)
- `tower.demo@demo.com` (torre de control)
- `admin.demo@demo.com` (administrador; lo usa el simulador GPS)

## Verificación

```powershell
dotnet build Siniestros360.slnx
dotnet test tests/Siniestros360.Tests.Unit
dotnet test tests/Siniestros360.Tests.Contracts
dotnet test tests/Siniestros360.Tests.Integration   # requiere Docker
dotnet test tests/Siniestros360.LocationSimulator.Tests
npm run build --prefix apps/insured-app
npm run build --prefix apps/adjuster-app
npm run build --prefix apps/control-tower
```

Recorrido de punta a punta con Playwright, con el AppHost arriba (detalle en [tests/README.md](tests/README.md)):

```powershell
npm ci --prefix tests/e2e
$env:PW_CHANNEL = "chrome"   # usa el Chrome instalado; sin esta variable: npx --prefix tests/e2e playwright install chromium
npm test --prefix tests/e2e
```

## Qué conceptos técnicos demuestra Siniestros360

ASP.NET Core Minimal APIs; microservicios por bounded context y migración desde un monolito modular; YARP y service discovery con Aspire; MassTransit sobre Azure Service Bus con topics, una cola por servicio y DLQ; arquitectura orientada a eventos; bus outbox, consumer outbox e inbox transaccionales de EF Core; saga de asignación como máquina de estado de MassTransit, con compensación y concurrencia optimista; idempotencia HTTP por atributo `[Idempotent]` con transacción local y rollback; JWT y RBAC con validación de propiedad; PostgreSQL/PostGIS y migraciones EF Core por servicio; SignalR con grupos por rol; workers de SLA; timeout, retry, circuit breaker y fallback; OpenTelemetry, health checks y logs estructurados; ProblemDetails; CQRS ligero; máquina de estados; object storage; preparación para Key Vault, Blob Storage y Application Insights; pruebas unitarias e integración con PostgreSQL real (Testcontainers); CI básico.
