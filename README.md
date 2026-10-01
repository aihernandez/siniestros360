# Siniestros360

Siniestros360 es una demo técnica de atención de siniestros de autos para una sola aseguradora. El asegurado reporta un siniestro con su ubicación, el sistema asigna al ajustador disponible más cercano, la torre de control sigue el caso en tiempo real y el ajustador registra llegada, inicio y cierre. La validación de póliza es asíncrona y nunca bloquea la atención.

Está construida con .NET 10, Aspire, YARP, PostgreSQL/PostGIS, MassTransit sobre Azure Service Bus, SignalR y React/Vite con Capacitor.

## Arquitectura

- `Gateway`: YARP, JWT, rate limiting por ruta, CORS para apps móviles y correlación.
- `IdentityService`: usuarios, roles, JWT y refresh tokens.
- `ClaimsService`: expediente, folio, máquina de estados y bitácora.
- `AdjustersService`: catálogo, disponibilidad y estado operativo de ajustadores.
- `LocationService`: GPS con PostGIS y detección de GPS sin actualizar.
- `DispatchService`: saga de asignación y reasignación (máquina de estado de MassTransit).
- `PolicyService`: validación asíncrona con timeout, retry, circuit breaker y fallback.
- `SlaService`: plazos por etapa, avisos, vencimientos y escalación.
- `DocumentsService`: metadatos y almacenamiento local o Azure Blob.
- `OperationsService`: vistas de lectura de la torre y SignalR.
- `Messaging` y `Contracts`: configuración de MassTransit, idempotencia transaccional y contratos de integración.
- `AppHost`: topología local con Aspire.

Cada servicio tiene su propia base de datos y migraciones. Ningún servicio consulta tablas de otro.

## Requisitos

- .NET 10 SDK.
- Node.js 22.
- Docker Desktop (PostgreSQL local y pruebas de integración).
- Un namespace de Azure Service Bus tier Standard (Basic no admite topics). El emulador local no funciona con MassTransit 8, porque su API de administración usa otro puerto. El tier Standard tiene un cargo base de unos 10 USD al mes.

## Ejecución local

Crea el namespace con Azure CLI. La cadena necesita permiso Manage porque MassTransit crea su topología al arrancar (un topic por tipo de evento y una cola por servicio):

```powershell
az group create -n <grupo> -l southcentralus
az servicebus namespace create -g <grupo> -n <nombre-unico> -l southcentralus --sku Standard
$cs = az servicebus namespace authorization-rule keys list -g <grupo> --namespace-name <nombre-unico> -n RootManageSharedAccessKey --query primaryConnectionString -o tsv
```

Configura una sola vez los secretos del AppHost (quedan en user-secrets, fuera del repositorio):

```powershell
dotnet user-secrets set "Parameters:jwt-signing-key" "<clave-de-al-menos-32-caracteres>" --project Siniestros360.AppHost
dotnet user-secrets set "ConnectionStrings:servicebus" $cs --project Siniestros360.AppHost
```

Levanta todo:

```powershell
aspire run
# o bien
dotnet run --project Siniestros360.AppHost
```

| Recurso | URL |
|---|---|
| Dashboard de Aspire | https://localhost:17025 |
| Gateway | http://localhost:5090 |
| Apps Vite (asegurado, ajustador, torre) | Puerto asignado por Aspire; aparece en el dashboard |

Apaga el AppHost cuando no lo uses: el simulador GPS genera alrededor de 1.3 millones de operaciones por día en Service Bus.

Usuarios demo, todos con contraseña `Demo!2026`:

- `insured.demo@demo.com` (asegurado)
- `adjuster.demo@demo.com` (Ajustador 01)
- `tower.demo@demo.com` (torre de control)
- `admin.demo@demo.com` (administrador; lo usa el simulador GPS)

## Verificación

```powershell
dotnet build Siniestros360.slnx
dotnet test Siniestros360.Tests.Unit/Siniestros360.Tests.Unit.csproj
dotnet test Siniestros360.Tests.Integration/Siniestros360.Tests.Integration.csproj   # requiere Docker
dotnet test Siniestros360.LocationSimulator.Tests/Siniestros360.LocationSimulator.Tests.csproj
npm run build --prefix Siniestros360.CustomerApp
npm run build --prefix Siniestros360.AdjusterApp
npm run build --prefix Siniestros360.ControlTower
```

## Qué conceptos técnicos demuestra Siniestros360

ASP.NET Core Minimal APIs; microservicios por bounded context y migración desde un monolito modular; YARP y service discovery con Aspire; MassTransit sobre Azure Service Bus con topics, una cola por servicio y DLQ; arquitectura orientada a eventos; bus outbox, consumer outbox e inbox transaccionales de EF Core; saga de asignación como máquina de estado de MassTransit, con compensación y concurrencia optimista; idempotencia HTTP por atributo `[Idempotent]` con transacción local y rollback; JWT y RBAC con validación de propiedad; PostgreSQL/PostGIS y migraciones EF Core por servicio; SignalR con grupos por rol; workers de SLA; timeout, retry, circuit breaker y fallback; OpenTelemetry, health checks y logs estructurados; ProblemDetails; CQRS ligero; máquina de estados; object storage; preparación para Key Vault, Blob Storage y Application Insights; pruebas unitarias e integración con PostgreSQL real (Testcontainers); CI básico.
