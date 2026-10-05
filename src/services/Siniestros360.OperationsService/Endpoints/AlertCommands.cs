using Siniestros360.ServiceDefaults.Endpoints;
using Siniestros360.SharedKernel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Siniestros360.OperationsService.Application;
using Siniestros360.OperationsService.Hubs;
using Siniestros360.OperationsService.Infrastructure;

namespace Siniestros360.OperationsService.Endpoints;

// El único comando de Operations: atender una alerta. No cambia el estado de otro servicio.
public static class AlertCommands
{
    internal sealed class AcknowledgeAlertEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/alerts/{alertId:guid}/acknowledge", AcknowledgeAsync)
                .RequireAuthorization(Policies.ControlTower)
                .WithName("AcknowledgeAlert")
                .WithSummary("Atender una alerta")
                .WithDescription("Idempotente: repetirlo devuelve la misma alerta con la primera fecha de atención.");
        }
    }

    private static async Task<Results<Ok<AlertView>, NotFound>> AcknowledgeAsync(
        Guid alertId,
        IUserContext userContext,
        OperationsDbContext database,
        IHubContext<OperationsHub> hubContext,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        var alert = await database.Alerts.SingleOrDefaultAsync(x => x.Id == alertId, cancellationToken);
        if (alert is null) return TypedResults.NotFound();
        if (alert.AcknowledgedAt is null)
        {
            alert.AcknowledgedAt = dateTimeProvider.UtcNow;
            alert.AcknowledgedBy = userContext.DisplayName;
            await database.SaveChangesAsync(cancellationToken);
            await hubContext.Clients.Group(OperationsHub.TowerGroup).SendAsync("alertAcknowledged", AlertView.From(alert), cancellationToken);
        }
        return TypedResults.Ok(AlertView.From(alert));
    }
}
