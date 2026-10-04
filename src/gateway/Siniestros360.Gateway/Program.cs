using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();

// Límites por política de ruta (ReverseProxy:Routes:*:RateLimiterPolicy). Particiona por usuario autenticado o, sin token, por IP.
// Las lecturas (GET) de rutas de comandos no se limitan con la política de comandos.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(ClientIp(http), _ => Window(builder.Configuration.GetValue("RateLimits:LoginPerMinute", 10))));
    options.AddPolicy("gps", http => RateLimitPartition.GetTokenBucketLimiter(Caller(http), _ => Bucket(builder.Configuration.GetValue("RateLimits:GpsPerMinute", 300))));
    options.AddPolicy("commands", http => HttpMethods.IsGet(http.Request.Method)
        ? RateLimitPartition.GetNoLimiter("reads")
        : RateLimitPartition.GetTokenBucketLimiter(Caller(http), _ => Bucket(builder.Configuration.GetValue("RateLimits:CommandsPerMinute", 60))));
    options.AddPolicy("documents", http => HttpMethods.IsGet(http.Request.Method)
        ? RateLimitPartition.GetNoLimiter("reads")
        : RateLimitPartition.GetTokenBucketLimiter(Caller(http), _ => Bucket(builder.Configuration.GetValue("RateLimits:DocumentsPerMinute", 20))));
    options.AddPolicy("reads", http => RateLimitPartition.GetTokenBucketLimiter(Caller(http), _ => Bucket(builder.Configuration.GetValue("RateLimits:ReadsPerMinute", 600))));
});

// Orígenes de las apps empaquetadas con Capacitor. En desarrollo web las apps usan el proxy de Vite y no necesitan CORS.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials().WithExposedHeaders("X-Correlation-ID", "Idempotent-Replayed")));

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy")).AddServiceDiscoveryDestinationResolver();
var app = builder.Build();
app.UseCors();
app.UseSiniestrosApiDefaults();
app.UseWebSockets();
app.UseRateLimiter();
app.MapGet("/gateway/info", () => Results.Ok(new { service = "Siniestros360.Gateway", responsibilities = new[] { "YARP routing", "JWT validation", "rate limiting", "correlation propagation", "service discovery" } })).AllowAnonymous();
app.MapReverseProxy();
app.Run();

static string Caller(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ClientIp(http);
static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
static FixedWindowRateLimiterOptions Window(int perMinute) => new() { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };
static TokenBucketRateLimiterOptions Bucket(int perMinute) => new() { TokenLimit = perMinute, TokensPerPeriod = perMinute, ReplenishmentPeriod = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true };

public partial class Program;
