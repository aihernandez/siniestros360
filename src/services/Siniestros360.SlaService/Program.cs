using Microsoft.EntityFrameworkCore;
using Siniestros360.SlaService.Application;
using Siniestros360.SlaService.Infrastructure;
using Siniestros360.SlaService.Workers;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
builder.Services.AddDbContext<SlaDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("sladb");
    if (string.IsNullOrWhiteSpace(connection)) options.UseInMemoryDatabase("sla-development"); else options.UseNpgsql(connection);
});
builder.Services.AddSingleton<SlaPolicy>();
builder.Services.AddHostedService<SlaMonitoringWorker>();
builder.AddReliableMessaging<SlaDbContext>("sla-service", bus => bus.AddConsumer<SlaEventsConsumer>());

var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<SlaDbContext>();
app.Run();

public partial class Program;
