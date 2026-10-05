using Microsoft.EntityFrameworkCore;
using Siniestros360.LocationService.Application;
using Siniestros360.LocationService.Endpoints;
using Siniestros360.LocationService.Infrastructure;
using Siniestros360.LocationService.Workers;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
// En este ensamblado: el generador de validación sólo descubre los tipos de los endpoints del proyecto que lo llama.
builder.Services.AddValidation();
builder.Services.AddDbContext<LocationDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("locationsdb");
    if (string.IsNullOrWhiteSpace(connection)) options.UseInMemoryDatabase("locations-development"); else options.UseNpgsql(connection, npgsql => npgsql.UseNetTopologySuite());
});
builder.AddReliableMessaging<LocationDbContext>("location-service");
builder.Services.AddScoped<ILocationEventSink, BusLocationEventSink>();
builder.Services.AddHostedService<GpsStaleWorker>();
var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<LocationDbContext>();

app.MapGroup("/api/v1/locations")
    .WithTags("Locations")
    .MapLocationEndpoints();

app.Run();

public partial class Program;
