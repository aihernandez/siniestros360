using System.Text.Json;
using Siniestros360.LocationSimulator.Hubs;
using Siniestros360.LocationSimulator.Options;
using Siniestros360.LocationSimulator.Publishing;
using Siniestros360.LocationSimulator.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<SimulatorOptions>()
    .Bind(builder.Configuration.GetSection(SimulatorOptions.SectionName))
    .Validate(options => options.AdjusterCount is >= 1 and <= 1_000,
        "Simulator:AdjusterCount debe estar entre 1 y 1000.")
    .Validate(options => options.UpdateIntervalMilliseconds is >= 100 and <= 300_000,
        "Simulator:UpdateIntervalMilliseconds debe estar entre 100 y 300000.")
    .Validate(options => options.MinSpeedKph > 0 && options.MaxSpeedKph >= options.MinSpeedKph,
        "El rango de velocidad configurado no es válido.")
    .Validate(options => !options.Forwarding.Enabled ||
                         Uri.TryCreate(options.Forwarding.Endpoint, UriKind.Absolute, out _),
        "Simulator:Forwarding:Endpoint debe ser una URL absoluta cuando el reenvío está habilitado.")
    .ValidateOnStart();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    }
}));

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<RouteCatalog>();
builder.Services.AddSingleton<SimulationStateStore>();
builder.Services.AddHttpClient(LocationPublisher.HttpClientName);
builder.Services.AddSingleton<ILocationPublisher, LocationPublisher>();
builder.Services.AddHostedService<LocationSimulationWorker>();

var app = builder.Build();

app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    service = "Siniestros360 Location Simulator",
    endpoints = new
    {
        adjusters = "/api/simulator/adjusters",
        status = "/api/simulator/status",
        hub = "/hubs/locations"
    }
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    checkedAt = DateTimeOffset.UtcNow
}));

var simulator = app.MapGroup("/api/simulator");

simulator.MapGet("/status", (SimulationStateStore store) => Results.Ok(new
{
    running = !store.IsPaused,
    adjusterCount = store.Count,
    generatedBatches = store.GeneratedBatches,
    lastUpdatedAt = store.LastUpdatedAt
}));

simulator.MapGet("/adjusters", (SimulationStateStore store) =>
    Results.Ok(store.GetSnapshots()));

simulator.MapGet("/adjusters/{adjusterId}", (string adjusterId, SimulationStateStore store) =>
{
    var adjuster = store.GetSnapshot(adjusterId);
    return adjuster is null ? Results.NotFound() : Results.Ok(adjuster);
});

simulator.MapPost("/control/pause", (SimulationStateStore store) =>
{
    store.Pause();
    return Results.Ok(new { running = false });
});

simulator.MapPost("/control/resume", (SimulationStateStore store) =>
{
    store.Resume();
    return Results.Ok(new { running = true });
});

simulator.MapPost("/control/reset", (SimulationStateStore store) =>
{
    store.Reset();
    return Results.Ok(new
    {
        running = !store.IsPaused,
        adjusterCount = store.Count,
        adjusters = store.GetSnapshots()
    });
});

app.MapHub<LocationHub>("/hubs/locations");

app.Run();

public partial class Program;
