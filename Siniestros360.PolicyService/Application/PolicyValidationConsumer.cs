using System.Diagnostics;
using System.Diagnostics.Metrics;
using MassTransit;
using Siniestros360.Contracts.Events;
using Siniestros360.PolicyService.Domain;
using Siniestros360.PolicyService.Infrastructure;

namespace Siniestros360.PolicyService.Application;

// El resultado nunca bloquea el alta del siniestro: si el proveedor no responde tras retry y circuit breaker
// se publica PolicyValidationUnavailable como fallback explícito, no una cobertura inventada.
public sealed class PolicyValidationConsumer(PolicyDbContext db, IPolicyProviderClient client) : IConsumer<PolicyValidationRequested>
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
        var now = DateTimeOffset.UtcNow;
        try
        {
            var result = await client.ValidateAsync(e.PolicyNumber, ct);
            db.Validations.Add(new PolicyValidation { ClaimId = e.ClaimId, PolicyNumber = e.PolicyNumber, Status = result.CoverageStatus, Reason = result.Reason, ValidatedAt = now });
            db.Attempts.Add(new ExternalValidationAttempt { Id = Guid.NewGuid(), ClaimId = e.ClaimId, Result = result.CoverageStatus, OccurredAt = now });
            await context.Publish(new PolicyValidationCompleted(e.ClaimId, e.PolicyNumber, result.CoverageStatus, result.Reason, now), ct);
            Validated.Add(1);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TimeoutException or TaskCanceledException or Polly.ExecutionRejectedException) && !ct.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            db.Attempts.Add(new ExternalValidationAttempt { Id = Guid.NewGuid(), ClaimId = e.ClaimId, Result = "Unavailable", Error = ex.Message, OccurredAt = now });
            await context.Publish(new PolicyValidationUnavailable(e.ClaimId, e.PolicyNumber, "External policy provider unavailable after retries/circuit breaker.", now), ct);
            Unavailable.Add(1);
        }

        await db.SaveChangesAsync(ct);
    }
}
