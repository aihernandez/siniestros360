using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Siniestros360.Messaging.Persistence;
using Testcontainers.PostgreSql;
using Claim = System.Security.Claims.Claim;
using ClaimTypes = System.Security.Claims.ClaimTypes;

namespace Siniestros360.Tests.Integration;

// Un solo PostgreSQL/PostGIS por corrida de pruebas; cada servicio usa su propia base, como en Aspire.
// Las pruebas usan PostgreSQL real porque EF InMemory ignora transacciones y no puede demostrar un rollback.
public static class Postgres
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(async () =>
    {
        var container = new PostgreSqlBuilder("postgis/postgis:16-3.4").Build();
        await container.StartAsync();
        return container;
    });

    public static string ConnectionString(string database)
        => new Npgsql.NpgsqlConnectionStringBuilder(Container.Value.GetAwaiter().GetResult().GetConnectionString()) { Database = database }.ConnectionString;
}

// Levanta un servicio real (migraciones, MassTransit con transporte en memoria, outbox e inbox sobre PostgreSQL).
public class ServiceFactory<TProgram>(string connectionName) : WebApplicationFactory<TProgram> where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:SigningKey", Tokens.Key);
        builder.UseSetting($"ConnectionStrings:{connectionName}", Postgres.ConnectionString($"{connectionName}_{Guid.NewGuid():N}"));
    }

    public HttpClient ClientFor(string subject, string role, Guid? adjusterId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Tokens.For(subject, role, adjusterId));
        return client;
    }

    // Entrega un evento por el bus del servicio. Repetir el mismo MessageId simula una redelivery del broker.
    public Task Deliver<TEvent>(TEvent integrationEvent, Guid? messageId = null) where TEvent : class
        => Services.GetRequiredService<IBus>().Publish(integrationEvent, context => context.MessageId = messageId ?? Guid.NewGuid());

    public async Task<T> Query<TDbContext, T>(Func<TDbContext, Task<T>> query) where TDbContext : DbContext
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TDbContext>());
    }
}

public static class Eventually
{
    public static async Task<T> Until<T>(Func<Task<T>> probe, Func<T, bool> done, int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var value = await probe();
        while (!done(value) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            value = await probe();
        }
        return value;
    }
}

public static class Tokens
{
    public const string Key = "integration-test-signing-key-at-least-32-characters-long";

    public static string For(string subject, string role, Guid? adjusterId = null)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, subject), new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.Role, role) };
        if (adjusterId is not null) claims.Add(new Claim(Siniestros360.Contracts.Messaging.ClaimTypes.AdjusterId, adjusterId.Value.ToString()));
        var token = new JwtSecurityToken("Siniestros360.Identity", "Siniestros360", claims, expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public static class Requests
{
    public static HttpRequestMessage Post(string url, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    public static string NewKey() => Guid.NewGuid().ToString();
}

// Falla inyectada justo antes del commit: cuando el filtro idempotente intenta guardar la respuesta final.
// Simula un error después de que el negocio y el outbox ya se escribieron dentro de la transacción.
public sealed class FailBeforeCommit : SaveChangesInterceptor
{
    public bool Enabled { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && eventData.Context!.ChangeTracker.Entries<IdempotencyRecord>().Any(x => x.State == EntityState.Modified && x.Entity.StatusCode != 0))
            throw new InvalidOperationException("Injected failure before commit.");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
