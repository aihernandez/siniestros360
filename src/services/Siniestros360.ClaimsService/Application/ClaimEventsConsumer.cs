using MassTransit;
using Microsoft.EntityFrameworkCore;
using Siniestros360.ClaimsService.Domain;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Events;

namespace Siniestros360.ClaimsService.Application;

// Eventos de otros contextos que modifican el expediente. Claims sigue siendo el único dueño del estado del siniestro.
// El consumer outbox de MassTransit envuelve cada Consume: inbox (deduplicación), cambio y SaveChanges en una transacción.
public sealed class ClaimEventsConsumer(ClaimsDbContext db) :
    IConsumer<AdjusterAssigned>,
    IConsumer<ClaimReassigned>,
    IConsumer<NoAdjusterAvailable>,
    IConsumer<PolicyValidationCompleted>,
    IConsumer<PolicyValidationUnavailable>,
    IConsumer<PolicyValidationFailed>,
    IConsumer<ClaimEscalated>
{
    public async Task Consume(ConsumeContext<AdjusterAssigned> context)
    {
        var e = context.Message;
        var claim = await Load(e.ClaimId, context.CancellationToken);
        if (claim.TryAssign(e.AdjusterId, e.AssignedAt, isReassignment: false))
            ClaimsTelemetry.AssignmentDuration.Record((e.AssignedAt - claim.ReportedAt).TotalSeconds);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public Task Consume(ConsumeContext<ClaimReassigned> context)
        => Apply(context.Message.ClaimId, claim => claim.TryAssign(context.Message.NewAdjusterId, context.Message.ReassignedAt, isReassignment: true), context.CancellationToken);

    public Task Consume(ConsumeContext<NoAdjusterAvailable> context)
        => Apply(context.Message.ClaimId, claim => claim.RecordDispatchIssue(context.Message.Reason, context.Message.OccurredAt), context.CancellationToken);

    public Task Consume(ConsumeContext<PolicyValidationCompleted> context)
        => Apply(context.Message.ClaimId, claim => claim.SetCoverage(context.Message.CoverageStatus, context.Message.Reason, context.Message.ValidatedAt), context.CancellationToken);

    public Task Consume(ConsumeContext<PolicyValidationUnavailable> context)
        => Apply(context.Message.ClaimId, claim => claim.SetCoverage("Unavailable", context.Message.Reason, context.Message.OccurredAt), context.CancellationToken);

    public Task Consume(ConsumeContext<PolicyValidationFailed> context)
        => Apply(context.Message.ClaimId, claim => claim.SetCoverage("Failed", context.Message.ErrorMessage, context.Message.FailedAt), context.CancellationToken);

    public Task Consume(ConsumeContext<ClaimEscalated> context)
        => Apply(context.Message.ClaimId, claim => claim.Escalate(context.Message.Reason, context.Message.EscalatedAt), context.CancellationToken);

    private async Task Apply(Guid claimId, Action<Claim> change, CancellationToken ct)
    {
        change(await Load(claimId, ct));
        await db.SaveChangesAsync(ct);
    }

    private async Task<Claim> Load(Guid claimId, CancellationToken ct)
        => await db.Claims.Include(x => x.Timeline).SingleOrDefaultAsync(x => x.Id == claimId, ct)
            ?? throw new InvalidOperationException($"Claim {claimId} does not exist.");
}
