# Servicios y patrones de Siniestros360

`Siniestros360.AppHost` levanta nueve servicios, el gateway, PostgreSQL/PostGIS, el simulador GPS y tres aplicaciones web. Cada servicio conserva sus datos y migraciones; los clientes HTTP entran por el gateway y la coordinación entre servicios usa eventos de MassTransit.

## Responsabilidades

| Componente | Responsabilidad | Superficie HTTP de negocio |
|---|---|---|
| `Gateway` | YARP, autenticación JWT, límites por ruta y descubrimiento de destinos de Aspire | Recibe `/api/v{version}/...` y los hubs |
| `IdentityService` | Usuarios, roles, inicio de sesión y renovación de tokens | `/api/v1/auth` |
| `ClaimsService` | Expediente, folio, transiciones de estado y bitácora | `/api/v1/claims` |
| `AdjustersService` | Catálogo, disponibilidad y estado de ajustadores | `/api/v1/adjusters` |
| `LocationService` | Posiciones GPS, consultas geográficas y detección de GPS antiguo | `/api/v1/locations` |
| `DispatchService` | Orquestación de asignación, reasignación y validación de póliza; reservas y compensación | `/api/v1/dispatch/claims` |
| `PolicyService` | Validación asíncrona de cobertura con resiliencia frente al proveedor | Consumidores de eventos; sin REST de negocio |
| `SlaService` | Seguimiento de plazos, avisos y escalaciones | Consumidores y worker; sin REST de negocio |
| `DocumentsService` | Metadatos y archivos en almacenamiento local o Azure Blob | `/api/v1/documents` |
| `OperationsService` | Proyecciones de lectura, alertas y actualizaciones SignalR para la torre | `/api/v1/operations` y `/hubs/operations` |
| `LocationSimulator` | Posiciones GPS de demostración y envío por el gateway | `/api/v1/simulator` |

`ServiceDefaults` contiene los registros y convenciones HTTP comunes. `SharedKernel` define `Result`, comandos, consultas, handlers y decoradores. `Contracts` contiene eventos compartidos y `ApiVersions.V1`; `Messaging` configura MassTransit, correlación, idempotencia y persistencia de mensajes.

## Endpoint y caso de uso

Los servicios REST agrupan sus rutas en clases `internal sealed` que implementan `IEndpoint`. `AddEndpoints(typeof(Program).Assembly)` registra las clases del servicio por reflexión y `MapEndpoints(...)` las mapea en el grupo versionado. En el piloto CQRS de `ClaimsService` hay una clase por caso de uso: el endpoint recibe la petición, crea el comando o la consulta, inyecta su handler y convierte `Result` en una respuesta HTTP. La lógica de negocio vive en el handler o en el dominio.

```csharp
internal sealed class Report : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/", async (
            ReportClaimRequest request,
            ICommandHandler<ReportClaimCommand, ClaimResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new ReportClaimCommand(
                request.PolicyNumber,
                request.VehiclePlate,
                request.IncidentType,
                request.Latitude,
                request.Longitude,
                request.RequiresAmbulance);

            Result<ClaimResponse> result = await handler.Handle(command, cancellationToken);

            return result.Match(
                claim => Results.Created(ApiVersions.V1Path($"claims/{claim.Id}"), claim),
                CustomResults.Problem);
        })
        .RequireAuthorization(Policies.ClaimReporters)
        .WithName("ReportClaim");
    }
}
```

El formato usa método con cuerpo, parámetros de la lambda en líneas separadas, `command`/`query` y `result` como nombres locales, y convenciones encadenadas después de `MapPost`/`MapGet`. El ejemplo abreviado corresponde a `ClaimsService/Endpoints/Claims/Report.cs`.

## CQRS, errores y tiempo

En el piloto CQRS de `ClaimsService`, los comandos cambian estado y las consultas leen. `AddApplication(assembly)` descubre handlers con Scrutor, registra validadores de FluentValidation y agrega decoradores de validación e instrumentación. No hay mediador: el endpoint inyecta directamente `ICommandHandler` o `IQueryHandler`. Los handlers devuelven `Result`/`Result<T>`; `CustomResults.Problem` traduce errores de dominio a Problem Details con un código estable.

`IDateTimeProvider` se registra en `ServiceDefaults` con `internal sealed class DateTimeProvider` y su propiedad `UtcNow`. Los servicios y la saga lo reciben por DI; las pruebas pueden proporcionar un reloj fijo.

## Versiones y comunicación

`ApiVersions.V1` define la versión mayor publicada. `MapApiVersion("claims", ApiVersions.V1)` agrega la ruta `/api/v{version:apiVersion}/claims` y sus metadatos; `ApiVersions.V1Path(...)` construye URLs devueltas por los endpoints y usadas por el simulador. Cada servicio REST publica OpenAPI por versión en desarrollo. El gateway mantiene el segmento de versión en la ruta y el servicio decide si la admite.

YARP resuelve destinos como `http://claims-service` mediante `Microsoft.Extensions.ServiceDiscovery.Yarp`; Aspire registra los recursos y propaga la configuración de descubrimiento. Los clientes web y móviles hacen peticiones al gateway. Los servicios intercambian hechos y órdenes de negocio por Azure Service Bus mediante MassTransit, sin depender de URLs HTTP de otros servicios.

## Mensajería y saga

`AddReliableMessaging<TDbContext>` registra MassTransit con Azure Service Bus cuando hay conexión y con transporte en memoria para pruebas o ejecución aislada. Usa bus outbox para publicaciones nacidas de una operación HTTP y consumer outbox/inbox para mensajes recibidos. Una cola procesa negocio y sagas; otra cola por servicio separa telemetría GPS. Los mensajes fallidos terminan en la DLQ de Azure Service Bus después de los reintentos configurados.

`DispatchService` registra `AssignmentStateMachine` como saga de MassTransit persistida con EF Core y concurrencia optimista. `ClaimReported` inicia la instancia, publica solicitudes de validación de póliza y de asignación, y reserva un ajustador disponible. La cobertura se incorpora después sin detener la atención. Los eventos de llegada, SLA, reasignación, cierre o cancelación cambian la saga; la compensación libera la reserva anterior cuando corresponde. Saga, reservas y publicaciones comparten `DispatchDbContext` y su transacción con el outbox.

## Dependencias

Las versiones NuGet se administran en [`Directory.Packages.props`](../Directory.Packages.props); `TargetFramework` está en [`Directory.Build.props`](../Directory.Build.props). La tabla de versiones principales está en el [README raíz](../README.md#versiones-de-librerías). Las versiones JavaScript se declaran en `apps/*/package.json` y se fijan en sus `package-lock.json`.
