# Pruebas de Siniestros360

Ejecuta estos comandos desde la raíz del repositorio después de restaurar la solución con `dotnet restore Siniestros360.slnx`.

| Proyecto | Qué comprueba | Comando |
|---|---|---|
| `Siniestros360.Tests.Unit` | Dominio, casos de uso, reservas y saga sin infraestructura externa | `dotnet test tests/Siniestros360.Tests.Unit` |
| `Siniestros360.Tests.Contracts` | Contratos de mensajes compartidos | `dotnet test tests/Siniestros360.Tests.Contracts` |
| `Siniestros360.Tests.Architecture` | Límites y dependencias entre capas | `dotnet test tests/Siniestros360.Tests.Architecture` |
| `Siniestros360.Tests.Integration` | APIs, gateway, PostgreSQL real con Testcontainers, outbox y saga | `dotnet test tests/Siniestros360.Tests.Integration` |
| `Siniestros360.LocationSimulator.Tests` | API y generación de posiciones del simulador | `dotnet test tests/Siniestros360.LocationSimulator.Tests` |

Las pruebas de integración requieren Docker Desktop. `ApiVersioningTests` en Integración y Simulador comprueban el contrato público `/api/v1`; `EndpointRegistrationTests` comprueba el descubrimiento de endpoints.

`tests/e2e` ejecuta Playwright contra el AppHost completo, el gateway, las tres apps y Azure Service Bus. Necesita el AppHost iniciado, un namespace de Service Bus configurado y un navegador disponible:

```powershell
npm ci --prefix tests/e2e
$env:PW_CHANNEL = "chrome"
npm test --prefix tests/e2e
```

La CI compila la solución en Release, ejecuta los cinco proyectos .NET, verifica `dotnet format` y construye las tres apps. El recorrido e2e usa el secreto `SERVICEBUS_CONNECTION_STRING` cuando está configurado.
