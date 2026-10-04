using Microsoft.EntityFrameworkCore;
using Siniestros360.PolicyService.Application;
using Siniestros360.PolicyService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
builder.Services.AddDbContext<PolicyDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("policydb");
    if (string.IsNullOrWhiteSpace(connection)) options.UseInMemoryDatabase("policy-development"); else options.UseNpgsql(connection);
});
builder.Services.AddHttpClient<IPolicyProviderClient, PolicyProviderClient>(c => c.Timeout = TimeSpan.FromSeconds(3)).AddStandardResilienceHandler(options =>
{
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
    options.Retry.MaxRetryAttempts = 2;
    options.CircuitBreaker.MinimumThroughput = 3;
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(20);
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
});

builder.AddReliableMessaging<PolicyDbContext>("policy-service", bus => bus.AddConsumer<PolicyValidationConsumer>());

var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<PolicyDbContext>();
app.Run();

public partial class Program;
