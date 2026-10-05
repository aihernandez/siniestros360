using Microsoft.EntityFrameworkCore;
using Siniestros360.OperationsService.Application;
using Siniestros360.OperationsService.Endpoints;
using Siniestros360.OperationsService.Hubs;
using Siniestros360.OperationsService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
// En este ensamblado: el generador de validación sólo descubre los tipos de los endpoints del proyecto que lo llama.
builder.Services.AddValidation();
builder.Services.AddSignalR();
builder.Services.AddDbContext<OperationsDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("operationsdb");
    if (string.IsNullOrWhiteSpace(connection)) options.UseInMemoryDatabase("operations-development"); else options.UseNpgsql(connection);
});
builder.AddReliableMessaging<OperationsDbContext>("operations-service", bus => bus.AddConsumer<OperationsProjectionConsumer>(), typeof(AdjusterLocationConsumer));

var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<OperationsDbContext>();

app.MapGroup("/api/v1/operations")
    .WithTags("Operations")
    .MapOperationsQueries()
    .MapAlertCommands();

app.MapHub<OperationsHub>("/hubs/operations");
app.Run();

public partial class Program;
