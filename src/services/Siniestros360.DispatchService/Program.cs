using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Messaging;
using Siniestros360.DispatchService.Application;
using Siniestros360.DispatchService.Domain;
using Siniestros360.DispatchService.Infrastructure;
using Siniestros360.Messaging;
using Siniestros360.Messaging.Idempotency;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
builder.Services.AddDbContext<DispatchDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("dispatchdb");
    if (string.IsNullOrWhiteSpace(connection)) options.UseInMemoryDatabase("dispatch-development"); else options.UseNpgsql(connection);
});
builder.Services.AddScoped<AdjusterReservations>();
builder.AddReliableMessaging<DispatchDbContext>("dispatch-service", bus =>
{
    bus.AddConsumer<AdjusterProjectionConsumer>();
    // La saga comparte DbContext y transacción con las reservas y el outbox; xmin resuelve la concurrencia optimista.
    bus.AddSagaStateMachine<AssignmentStateMachine, AssignmentState>()
        .EntityFrameworkRepository(repository =>
        {
            repository.ConcurrencyMode = ConcurrencyMode.Optimistic;
            repository.ExistingDbContext<DispatchDbContext>();
            repository.UsePostgres();
        });
}, typeof(AdjusterLocationConsumer));

var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<DispatchDbContext>();

var dispatch = app.MapGroup("/api/v1/dispatch/claims").RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.ControlTower},{Roles.Admin}" }).WithIdempotency();
dispatch.MapGet("/{claimId:guid}", async (Guid claimId, DispatchDbContext db, CancellationToken ct) =>
{
    var saga = await db.AssignmentSagas.AsNoTracking().SingleOrDefaultAsync(x => x.CorrelationId == claimId, ct);
    if (saga is null) return Results.NotFound();
    var attempts = await db.Attempts.AsNoTracking().Where(x => x.ClaimId == claimId).OrderBy(x => x.OccurredAt).ToListAsync(ct);
    return Results.Ok(new { claimId, status = saga.CurrentState, saga.AdjusterId, saga.PreviousAdjusterId, saga.DistanceKm, saga.StartedAt, saga.UpdatedAt, attempts });
});

// Los comandos de la torre se entregan a la saga como mensajes: la saga es la única que cambia su estado. Respuesta 202.
dispatch.MapPost("/{claimId:guid}/assign", [Idempotent] async (Guid claimId, ManualAssignmentRequest request, HttpContext http, DispatchDbContext db, IPublishEndpoint publish, CancellationToken ct) =>
{
    var saga = await db.AssignmentSagas.AsNoTracking().SingleOrDefaultAsync(x => x.CorrelationId == claimId, ct);
    if (saga is null) return Results.NotFound();
    if (saga.CurrentState != nameof(AssignmentStateMachine.Unavailable))
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: $"A claim in {saga.CurrentState} cannot be assigned manually; use reassign.");
    await publish.PublishCorrelated(new ManualAssignmentRequested(claimId, request.AdjusterId), Correlation.From(http), ct);
    await db.SaveChangesAsync(ct);
    return Results.Accepted($"/api/v1/dispatch/claims/{claimId}", new { claimId, status = "AssignmentRequested" });
});

dispatch.MapPost("/{claimId:guid}/reassign", [Idempotent] async (Guid claimId, ReassignmentRequest request, HttpContext http, DispatchDbContext db, IPublishEndpoint publish, CancellationToken ct) =>
{
    var saga = await db.AssignmentSagas.AsNoTracking().SingleOrDefaultAsync(x => x.CorrelationId == claimId, ct);
    if (saga is null) return Results.NotFound();
    if (saga.CurrentState != nameof(AssignmentStateMachine.Assigned))
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: $"A claim in {saga.CurrentState} cannot be reassigned.");
    var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Reasignado por la torre de control." : request.Reason.Trim();
    await publish.PublishCorrelated(new ManualReassignmentRequested(claimId, reason), Correlation.From(http), ct);
    await db.SaveChangesAsync(ct);
    return Results.Accepted($"/api/v1/dispatch/claims/{claimId}", new { claimId, status = "ReassignmentRequested" });
});

app.Run();

public sealed record ManualAssignmentRequest(Guid? AdjusterId);
public sealed record ReassignmentRequest(string? Reason);
public partial class Program;
