using MassTransit;
using Siniestros360.ClaimsService.Domain;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Events;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.Report;

// Folio inmediato: la póliza se valida y el ajustador se asigna después, por eventos. Los eventos se guardan en el outbox
// con el SaveChanges y salen al broker sólo después del commit.
internal sealed class ReportClaimCommandHandler(ClaimsDbContext db, IUserContext user, IPublishEndpoint publish, TimeProvider clock)
    : ICommandHandler<ReportClaimCommand, ClaimResponse>
{
    public async Task<Result<ClaimResponse>> Handle(ReportClaimCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var claim = Claim.Report(user.UserId, command.PolicyNumber, command.VehiclePlate, command.IncidentType, command.Latitude, command.Longitude, command.RequiresAmbulance, now);
        db.Claims.Add(claim);
        await publish.Publish(new ClaimReported(claim.Id, claim.InsuredId, claim.Folio, claim.PolicyNumber, claim.VehiclePlate, claim.IncidentType, claim.Latitude, claim.Longitude, claim.RequiresAmbulance, now), cancellationToken);
        await publish.Publish(new PolicyValidationRequested(claim.Id, claim.PolicyNumber, now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        ClaimsTelemetry.Reported.Add(1);
        return ClaimResponse.From(claim);
    }
}
