using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("postgis/postgis", "16-3.4")
    .WithDataVolume("siniestros360-postgres-migrations");
var jwtKey = builder.AddParameter("jwt-signing-key", secret: true);

var identity = Service<Projects.Siniestros360_IdentityService>("identity-service", "identitydb", "siniestros360_identity");
var claims = Service<Projects.Siniestros360_ClaimsService>("claims-service", "claimsdb", "siniestros360_claims");
var adjusters = Service<Projects.Siniestros360_AdjustersService>("adjusters-service", "adjustersdb", "siniestros360_adjusters");
var locations = Service<Projects.Siniestros360_LocationService>("location-service", "locationsdb", "siniestros360_locations");
var dispatch = Service<Projects.Siniestros360_DispatchService>("dispatch-service", "dispatchdb", "siniestros360_dispatch");
var policy = Service<Projects.Siniestros360_PolicyService>("policy-service", "policydb", "siniestros360_policy");
var sla = Service<Projects.Siniestros360_SlaService>("sla-service", "sladb", "siniestros360_sla");
var documents = Service<Projects.Siniestros360_DocumentsService>("documents-service", "documentsdb", "siniestros360_documents");
var operations = Service<Projects.Siniestros360_OperationsService>("operations-service", "operationsdb", "siniestros360_operations");

// Namespace real de Azure Service Bus (tier Standard; Basic no admite topics). El emulador no sirve con MassTransit 8:
// su API de administración usa otro puerto y MassTransit sólo acepta una cadena de conexión.
// La cadena va en user-secrets del AppHost (ConnectionStrings:servicebus) y necesita permiso Manage, porque MassTransit
// crea su topología al arrancar: un topic por tipo de evento y una cola por servicio.
var serviceBus = builder.AddConnectionString("servicebus");
foreach (var service in new[] { claims, adjusters, locations, dispatch, policy, sla, documents, operations })
{
    service.WithReference(serviceBus).WaitFor(serviceBus);
}

// Los servicios no tienen launchSettings: sus endpoints se declaran aquí. El gateway usa el puerto fijo 5090
// (el emulador Android lo alcanza como 10.0.2.2:5090).
var gateway = builder.AddProject<Projects.Siniestros360_Gateway>("gateway")
    .WithHttpEndpoint(port: 5090, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithExternalHttpEndpoints()
    .WithEnvironment("Jwt__SigningKey", jwtKey)
    .WithReference(identity).WithReference(claims).WithReference(adjusters).WithReference(locations).WithReference(dispatch).WithReference(documents).WithReference(operations)
    .WaitFor(identity).WaitFor(claims).WaitFor(operations);
var gatewayUrl = gateway.GetEndpoint("http");

builder.AddProject<Projects.Siniestros360_LocationSimulator>("location-simulator")
    .WithEnvironment("Simulator__Forwarding__Enabled", "true")
    .WithEnvironment("Simulator__Forwarding__Endpoint", ReferenceExpression.Create($"{gatewayUrl}/api/v1/locations/batch"))
    .WithEnvironment("Simulator__Forwarding__LoginEndpoint", ReferenceExpression.Create($"{gatewayUrl}/api/v1/auth/login"))
    .WaitFor(gateway).WaitFor(adjusters).WaitFor(locations);

// Las apps llaman rutas relativas y el proxy de Vite las reenvía al gateway, así el navegador no necesita CORS.
builder.AddViteApp("insured-app", "../Siniestros360.CustomerApp").WithEnvironment("SINIESTROS360_GATEWAY_URL", gatewayUrl).WaitFor(gateway);
builder.AddViteApp("adjuster-app", "../Siniestros360.AdjusterApp").WithEnvironment("SINIESTROS360_GATEWAY_URL", gatewayUrl).WaitFor(gateway);
builder.AddViteApp("control-tower", "../Siniestros360.ControlTower").WithEnvironment("SINIESTROS360_GATEWAY_URL", gatewayUrl).WaitFor(gateway);

builder.Build().Run();

IResourceBuilder<ProjectResource> Service<TProject>(string name, string databaseResource, string databaseName) where TProject : IProjectMetadata, new()
{
    var database = postgres.AddDatabase(databaseResource, databaseName);
    return builder.AddProject<TProject>(name)
        .WithHttpEndpoint(name: "http")
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
        .WithReference(database).WaitFor(database)
        .WithEnvironment("Jwt__SigningKey", jwtKey)
        .WithHttpHealthCheck("/health", endpointName: "http");
}
