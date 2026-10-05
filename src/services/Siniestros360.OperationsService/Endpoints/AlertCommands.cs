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
    public static RouteGroupBuilder MapAlertCommands(this RouteGroupBuilder operations)
    {
        operations.MapPost("/alerts/{id:guid}/acknowledge", AcknowledgeAsync)
            .RequireAuthorization(Policies.ControlTower)
            .WithName("AcknowledgeAlert")
            .WithSummary("Atender una alerta")
            .WithDescription("Idempotente: repetirlo devuelve la misma alerta con la primera fecha de atención.");
        return operations;
    }

    private static async Task<Results<Ok<AlertView>, NotFound>> AcknowledgeAsync(Guid id, UserContext user, OperationsDbContext db, IHubContext<OperationsHub> hub, CancellationToken ct)
    {
        var alert = await db.Alerts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (alert is null) return TypedResults.NotFound();
        if (alert.AcknowledgedAt is null)
        {
            alert.AcknowledgedAt = DateTimeOffset.UtcNow;
            alert.AcknowledgedBy = user.DisplayName;
            await db.SaveChangesAsync(ct);
            await hub.Clients.Group(OperationsHub.TowerGroup).SendAsync("alertAcknowledged", AlertView.From(alert), ct);
        }
        return TypedResults.Ok(AlertView.From(alert));
    }
}
