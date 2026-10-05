using Microsoft.EntityFrameworkCore;
using Siniestros360.LocationService.Application;
using Siniestros360.LocationService.Infrastructure;
using Siniestros360.Contracts.Common;
using Siniestros360.LocationService.Workers;
using Siniestros360.ServiceDefaults.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();

builder.Services.AddValidation();
builder.Services.AddDbContext<LocationDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("locationsdb");
    if (string.IsNullOrWhiteSpace(connection)) options.UseInMemoryDatabase("locations-development"); else options.UseNpgsql(connection, npgsql => npgsql.UseNetTopologySuite());
});

builder.AddReliableMessaging<LocationDbContext>("location-service");
builder.Services.AddScoped<ILocationEventSink, BusLocationEventSink>();
builder.Services.AddHostedService<GpsStaleWorker>();
builder.Services.AddEndpoints(typeof(Program).Assembly);
var app = builder.Build();

app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<LocationDbContext>();

app.MapEndpoints(app.MapApiVersion("locations", ApiVersions.V1)
    .WithTags("Locations"));

app.Run();

public partial class Program;
