using System.Security.Claims;
using System.Threading.RateLimiting;

namespace Siniestros360.Gateway.Extensions;

// Nombres de las políticas que las rutas de YARP declaran en RateLimiterPolicy (appsettings.json).
public static class RateLimitingPolicies
{
    public const string Login = "login";
    public const string Gps = "gps";
    public const string Commands = "commands";
    public const string Documents = "documents";
    public const string Reads = "reads";
}

internal static class RateLimitingExtensions
{
    // Una política por tipo de ruta, particionada por usuario autenticado o, sin token, por IP. Además, un limitador global
    // de respaldo para toda petición: cubre rutas sin política propia (el hub de SignalR) y cualquiera que se agregue.
    // Las lecturas (GET) de rutas de comandos o documentos no consumen esas políticas.
    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        int PerMinute(string key, int fallback) => configuration.GetValue($"RateLimits:{key}", fallback);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                RateLimitPartition.GetTokenBucketLimiter(Caller(http), _ => Bucket(PerMinute("GlobalPerMinute", 1200))));
            options.AddPolicy(RateLimitingPolicies.Login, http => RateLimitPartition.GetFixedWindowLimiter(ClientIp(http), _ => Window(PerMinute("LoginPerMinute", 10))));
            options.AddPolicy(RateLimitingPolicies.Gps, http => RateLimitPartition.GetTokenBucketLimiter(Caller(http), _ => Bucket(PerMinute("GpsPerMinute", 300))));
            options.AddPolicy(RateLimitingPolicies.Commands, http => HttpMethods.IsGet(http.Request.Method)
                ? RateLimitPartition.GetNoLimiter("reads")
                : RateLimitPartition.GetTokenBucketLimiter(Caller(http), _ => Bucket(PerMinute("CommandsPerMinute", 60))));
            options.AddPolicy(RateLimitingPolicies.Documents, http => HttpMethods.IsGet(http.Request.Method)
                ? RateLimitPartition.GetNoLimiter("reads")
                : RateLimitPartition.GetTokenBucketLimiter(Caller(http), _ => Bucket(PerMinute("DocumentsPerMinute", 20))));
            options.AddPolicy(RateLimitingPolicies.Reads, http => RateLimitPartition.GetTokenBucketLimiter(Caller(http), _ => Bucket(PerMinute("ReadsPerMinute", 600))));
        });
        return services;
    }

    private static string Caller(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ClientIp(http);

    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static FixedWindowRateLimiterOptions Window(int perMinute) => new() { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };

    private static TokenBucketRateLimiterOptions Bucket(int perMinute) => new() { TokenLimit = perMinute, TokensPerPeriod = perMinute, ReplenishmentPeriod = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true };
}
