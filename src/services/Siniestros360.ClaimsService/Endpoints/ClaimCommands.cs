using MassTransit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Siniestros360.ClaimsService.Application;
using Siniestros360.ClaimsService.Domain;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Common;
using Siniestros360.Contracts.Events;
using Siniestros360.Messaging;
using Siniestros360.Messaging.Idempotency;

namespace Siniestros360.ClaimsService.Endpoints;

// Lado de escritura: cada comando cambia el agregado, publica sus eventos por el bus outbox y corre dentro de la
// transacción del filtro [Idempotent] (reserva de llave, negocio, outbox y respuesta se confirman juntos).
public static class ClaimCommands
{
    public static RouteGroupBuilder MapClaimCommands(this RouteGroupBuilder claims)
    {
        // La torre también reporta: su pestaña Pruebas genera siniestros reales para validar el despacho.
        claims.MapPost("/", ReportAsync)
            .RequireAuthorization(Policies.ClaimReporters)
            .WithName("ReportClaim")
            .WithSummary("Reportar un siniestro")
            .WithDescription("Responde con el folio de inmediato; la póliza se valida y el ajustador se asigna por eventos.");

        MapTransition(claims, "/{claimId:guid}/cancel", "CancelClaim", "Cancelar un siniestro", Policies.ClaimReporters, (claim, user, now) =>
        {
            var reason = user.IsInRole(Roles.Insured) ? "Cancelado por el asegurado." : "Cancelado por la torre de control.";
            claim.Cancel(reason, now);
            ClaimsTelemetry.Cancelled.Add(1);
            return new ClaimCancelled(claim.Id, reason, now);
        });
        MapTransition(claims, "/{claimId:guid}/adjuster-arrived", "RegisterArrival", "Registrar la llegada del ajustador", Policies.Adjuster, (claim, user, now) =>
        {
            claim.Arrive(RequiredAdjuster(user), now);
            return new AdjusterArrived(claim.Id, claim.AssignedAdjusterId!.Value, now);
        });
        MapTransition(claims, "/{claimId:guid}/service-started", "StartService", "Iniciar la atención", Policies.Adjuster, (claim, user, now) =>
        {
            claim.Start(RequiredAdjuster(user), now);
            return new AdjusterServiceStarted(claim.Id, claim.AssignedAdjusterId!.Value, now);
        });
        MapTransition(claims, "/{claimId:guid}/service-completed", "CompleteService", "Finalizar el servicio", Policies.Adjuster, (claim, user, now) =>
        {
            claim.Complete(RequiredAdjuster(user), now);
            ClaimsTelemetry.Closed.Add(1);
            return new AdjusterServiceCompleted(claim.Id, claim.AssignedAdjusterId!.Value, now);
        });
        return claims;
    }

    [Idempotent]
    private static async Task<Created<ClaimResponse>> ReportAsync(ReportClaimRequest request, HttpContext http, IUserContext user, ClaimsDbContext db, IPublishEndpoint publish, CancellationToken ct)
    {
        using var activity = ClaimsTelemetry.ActivitySource.StartActivity("Claim.Report");
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var claim = new Claim
        {
            Id = id,
            InsuredId = user.UserId,
            Folio = $"SIN-{now:yyyy}-{id.ToString("N")[..8].ToUpperInvariant()}",
            PolicyNumber = request.PolicyNumber,
            VehiclePlate = request.VehiclePlate,
            IncidentType = request.IncidentType,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RequiresAmbulance = request.RequiresAmbulance,
            Status = ClaimStatus.AssignmentPending,
            ReportedAt = now,
            UpdatedAt = now,
            Version = 1,
            Timeline = [new() { Id = Guid.NewGuid(), ClaimId = id, Type = "claim.reported", Details = "Claim reported.", OccurredAt = now }]
        };
        var correlationId = Correlation.From(http);
        activity?.SetTag("siniestros360.claim_id", id);
        db.Claims.Add(claim);
        // Bus outbox: los eventos se guardan con el SaveChanges y se envían al broker sólo después del commit.
        await publish.PublishCorrelated(new ClaimReported(claim.Id, claim.InsuredId, claim.Folio, claim.PolicyNumber, claim.VehiclePlate, claim.IncidentType, claim.Latitude, claim.Longitude, claim.RequiresAmbulance, now), correlationId, ct);
        await publish.PublishCorrelated(new PolicyValidationRequested(claim.Id, claim.PolicyNumber, now), correlationId, ct);
        await db.SaveChangesAsync(ct);
        ClaimsTelemetry.Reported.Add(1);
        return TypedResults.Created($"/api/v1/claims/{claim.Id}", ClaimResponse.From(claim));
    }

    // Una transición del siniestro: participa sólo quien la política permite (404 si no, para no revelar el siniestro),
    // el dominio decide si es legal (409) y el evento sale por el outbox junto con ClaimStatusChanged.
    private static void MapTransition<TEvent>(RouteGroupBuilder claims, string route, string name, string summary, string policy, Func<Claim, IUserContext, DateTimeOffset, TEvent> transition)
        where TEvent : class
    {
        var endpoint = claims.MapPost(route, [Idempotent] async Task<Results<Ok<ClaimResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> (
            Guid claimId, HttpContext http, IUserContext user, ClaimsDbContext db, IPublishEndpoint publish, CancellationToken ct) =>
        {
            var claim = await db.Claims.Include(x => x.Timeline).SingleOrDefaultAsync(x => x.Id == claimId, ct);
            if (claim is null || !await user.CanAccessClaimAsync(claim.Participants())) return TypedResults.NotFound();
            try
            {
                var previousStatus = claim.Status;
                var @event = transition(claim, user, DateTimeOffset.UtcNow);
                var correlationId = Correlation.From(http);
                await publish.PublishCorrelated(@event, correlationId, ct);
                await publish.PublishCorrelated(new ClaimStatusChanged(claim.Id, previousStatus.ToString(), claim.Status.ToString(), claim.Version, claim.UpdatedAt), correlationId, ct);
                if (claim.Status == ClaimStatus.Closed) await publish.PublishCorrelated(new ClaimClosed(claim.Id, claim.UpdatedAt), correlationId, ct);
                await db.SaveChangesAsync(ct);
                return TypedResults.Ok(ClaimResponse.From(claim));
            }
            catch (ClaimStateException exception) { return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Transición no permitida", detail: exception.Message); }
            catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
            catch (DbUpdateConcurrencyException) { return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "El siniestro cambió al mismo tiempo; vuelve a intentarlo."); }
        });
        endpoint.RequireAuthorization(policy).WithName(name).WithSummary(summary);
    }

    // Un ajustador sin adjuster_id en el token no puede actuar sobre ningún siniestro.
    private static Guid RequiredAdjuster(IUserContext user) => user.AdjusterId ?? throw new UnauthorizedAccessException("The token has no adjuster_id claim.");
}
