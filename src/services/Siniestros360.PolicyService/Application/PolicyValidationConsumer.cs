using Siniestros360.SharedKernel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MassTransit;
using Siniestros360.Contracts.Events;
using Siniestros360.PolicyService.Domain;
using Siniestros360.PolicyService.Infrastructure;

namespace Siniestros360.PolicyService.Application;

// El resultado nunca bloquea el alta del siniestro: si el proveedor no responde tras retry y circuit breaker
// se publica PolicyValidationUnavailable como fallback explícito, no una cobertura inventada.
public sealed class PolicyValidationConsumer(PolicyDbContext db, IPolicyProviderClient client, IDateTimeProvider clock) : IConsumer<PolicyValidationRequested>
{
    private static readonly ActivitySource ActivitySource = new("Siniestros360.PolicyService");
    private static readonly Meter Meter = new("Siniestros360.PolicyService");
    private static readonly Counter<long> Validated = Meter.CreateCounter<long>("policy.validated.count");
    private static readonly Counter<long> Unavailable = Meter.CreateCounter<long>("policy.unavailable.count");

    public async Task Consume(ConsumeContext<PolicyValidationRequested> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;
        using var activity = ActivitySource.StartActivity("Policy.Validate");
        activity?.SetTag("siniestros360.claim_id", e.ClaimId);
        var now = clock.UtcNow;
        try
        {
            var result = await client.ValidateAsync(e.PolicyNumber, ct);
            db.Validations.Add(new PolicyValidation { ClaimId = e.ClaimId, PolicyNumber = e.PolicyNumber, Status = result.CoverageStatus, Reason = result.Reason, ValidatedAt = now });
            db.Attempts.Add(new ExternalValidationAttempt { Id = Guid.NewGuid(), ClaimId = e.ClaimId, Result = result.CoverageStatus, OccurredAt = now });
            await context.Publish(new PolicyValidationCompleted(e.ClaimId, e.PolicyNumber, result.CoverageStatus, result.Reason, now), ct);
            Validated.Add(1);
        }
        catch (Exception ex) when (IsPermanent(ex) && !ct.IsCancellationRequested)
        {
            // Reintentar no lo arregla: sin este evento el mensaje terminaba en la DLQ y la cobertura quedaba en Pending.
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var code = ex is HttpRequestException { StatusCode: { } status } ? $"Provider{(int)status}" : "InvalidProviderResponse";
            db.Attempts.Add(new ExternalValidationAttempt { Id = Guid.NewGuid(), ClaimId = e.ClaimId, Result = "Failed", Error = ex.Message, OccurredAt = now });
            await context.Publish(new PolicyValidationFailed(e.ClaimId, e.PolicyNumber, code, "El proveedor de pólizas respondió con un error que no se resuelve reintentando.", now), ct);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TimeoutException or TaskCanceledException or Polly.ExecutionRejectedException) && !ct.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            db.Attempts.Add(new ExternalValidationAttempt { Id = Guid.NewGuid(), ClaimId = e.ClaimId, Result = "Unavailable", Error = ex.Message, OccurredAt = now });
            await context.Publish(new PolicyValidationUnavailable(e.ClaimId, e.PolicyNumber, "El proveedor de pólizas no respondió tras los reintentos.", now), ct);
            Unavailable.Add(1);
        }

        await db.SaveChangesAsync(ct);
    }

    // Respuesta ilegible o rechazo 4xx del proveedor (salvo 408 y 429, que sí son transitorios).
    private static bool IsPermanent(Exception exception) => exception switch
    {
        System.Text.Json.JsonException or NotSupportedException => true,
        HttpRequestException { StatusCode: { } status } => (int)status is >= 400 and < 500 and not 408 and not 429,
        _ => false
    };
}
