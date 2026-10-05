using Siniestros360.Gateway.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();

builder.Services.AddGatewayRateLimiting(builder.Configuration);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
.WithOrigins(allowedOrigins)
.AllowAnyHeader()
.AllowAnyMethod()
.AllowCredentials()
.WithExposedHeaders("X-Correlation-ID", "Idempotent-Replayed")));

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();
app.UseCors();
app.UseSiniestrosApiDefaults();
app.UseWebSockets();
app.UseRateLimiter();
app.MapGet("/gateway/info", () => Results.Ok(new { service = "Siniestros360.Gateway", responsibilities = new[] { "YARP routing", "JWT validation", "rate limiting", "correlation propagation", "service discovery" } })).AllowAnonymous();
app.MapReverseProxy();
app.Run();

public partial class Program;
