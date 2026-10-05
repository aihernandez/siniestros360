using MassTransit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Siniestros360.DispatchService.Application;
using Siniestros360.DispatchService.Domain;
using Siniestros360.DispatchService.Infrastructure;
using Siniestros360.Messaging;
using Siniestros360.Messaging.Idempotency;

namespace Siniestros360.DispatchService.Endpoints;

/// <summary>Un intento de asignación, reasignación o liberación.</summary>
public sealed record DispatchAttemptResponse(Guid Id, Guid ClaimId, Guid? AdjusterId, string Result, double? DistanceKm, DateTimeOffset OccurredAt);

/// <summary>Estado de la saga de asignación de un siniestro y su historial.</summary>
public sealed record DispatchStatusResponse(Guid ClaimId, string Status, Guid? AdjusterId, Guid? PreviousAdjusterId, double? DistanceKm, DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, IReadOnlyList<DispatchAttemptResponse> Attempts);

/// <summary>Comando aceptado: la saga lo procesa de forma asíncrona.</summary>
public sealed record DispatchCommandAccepted(Guid ClaimId, string Status);

/// <summary>Asignación manual; sin ajustador, la saga elige al más cercano.</summary>
public sealed record ManualAssignmentRequest(Guid? AdjusterId);

/// <summary>Reasignación manual con motivo opcional.</summary>
public sealed record ReassignmentRequest(string? Reason);

// Despacho es de la torre. Los comandos no cambian la saga: le entregan un mensaje, y la saga es la única que cambia su
// estado. Por eso responden 202 con la ruta de la consulta.
public static class DispatchEndpoints
{
    public static RouteGroupBuilder MapDispatchEndpoints(this RouteGroupBuilder dispatch)
    {
        dispatch.RequireAuthorization(Policies.ControlTower);

        // Consulta
        dispatch.MapGet("/{claimId:guid}", GetAsync).WithName("GetDispatchStatus").WithSummary("Estado de la asignación de un siniestro");

        // Comandos
        dispatch.MapPost("/{claimId:guid}/assign", AssignAsync).WithName("AssignManually").WithSummary("Asignar manualmente un siniestro sin ajustador disponible");
        dispatch.MapPost("/{claimId:guid}/reassign", ReassignAsync).WithName("ReassignManually").WithSummary("Reasignar un siniestro asignado");
        return dispatch;
    }

    private static async Task<Results<Ok<DispatchStatusResponse>, NotFound>> GetAsync(Guid claimId, DispatchDbContext db, CancellationToken ct)
    {
        var saga = await db.AssignmentSagas.AsNoTracking().SingleOrDefaultAsync(x => x.CorrelationId == claimId, ct);
        if (saga is null) return TypedResults.NotFound();
        var attempts = await db.Attempts.AsNoTracking()
            .Where(x => x.ClaimId == claimId)
            .OrderBy(x => x.OccurredAt)
            .Select(x => new DispatchAttemptResponse(x.Id, x.ClaimId, x.AdjusterId, x.Result, x.DistanceKm, x.OccurredAt))
            .ToListAsync(ct);
        return TypedResults.Ok(new DispatchStatusResponse(claimId, saga.CurrentState, saga.AdjusterId, saga.PreviousAdjusterId, saga.DistanceKm, saga.StartedAt, saga.UpdatedAt, attempts));
    }

    [Idempotent]
    private static async Task<Results<Accepted<DispatchCommandAccepted>, NotFound, ProblemHttpResult>> AssignAsync(
        Guid claimId, ManualAssignmentRequest request, HttpContext http, DispatchDbContext db, IPublishEndpoint publish, CancellationToken ct)
    {
        var saga = await db.AssignmentSagas.AsNoTracking().SingleOrDefaultAsync(x => x.CorrelationId == claimId, ct);
        if (saga is null) return TypedResults.NotFound();
        if (saga.CurrentState != nameof(AssignmentStateMachine.Unavailable))
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: $"Un siniestro en {saga.CurrentState} no se asigna manualmente; usa la reasignación.");
        await publish.PublishCorrelated(new ManualAssignmentRequested(claimId, request.AdjusterId), Correlation.From(http), ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Accepted($"/api/v1/dispatch/claims/{claimId}", new DispatchCommandAccepted(claimId, "AssignmentRequested"));
    }

    [Idempotent]
    private static async Task<Results<Accepted<DispatchCommandAccepted>, NotFound, ProblemHttpResult>> ReassignAsync(
        Guid claimId, ReassignmentRequest request, HttpContext http, DispatchDbContext db, IPublishEndpoint publish, CancellationToken ct)
    {
        var saga = await db.AssignmentSagas.AsNoTracking().SingleOrDefaultAsync(x => x.CorrelationId == claimId, ct);
        if (saga is null) return TypedResults.NotFound();
        if (saga.CurrentState != nameof(AssignmentStateMachine.Assigned))
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: $"Un siniestro en {saga.CurrentState} no se puede reasignar.");
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Reasignado por la torre de control." : request.Reason.Trim();
        await publish.PublishCorrelated(new ManualReassignmentRequested(claimId, reason), Correlation.From(http), ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Accepted($"/api/v1/dispatch/claims/{claimId}", new DispatchCommandAccepted(claimId, "ReassignmentRequested"));
    }
}
