using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Siniestros360.ClaimsService.Application;
using Siniestros360.ClaimsService.Domain;
using Siniestros360.ClaimsService.Infrastructure;
using Siniestros360.Contracts.Events;
using Siniestros360.Contracts.Common;
using Siniestros360.Messaging;
using Siniestros360.Messaging.Idempotency;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
builder.Services.AddDbContext<ClaimsDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("claimsdb");
    if (string.IsNullOrWhiteSpace(connectionString)) options.UseInMemoryDatabase("claims-development");
    else options.UseNpgsql(connectionString);
});
builder.AddReliableMessaging<ClaimsDbContext>("claims-service", bus => bus.AddConsumer<ClaimEventsConsumer>());

var app = builder.Build();
app.UseSiniestrosApiDefaults();
await app.InitializeDatabaseAsync<ClaimsDbContext>();

var claims = app.MapGroup("/api/v1/claims").RequireAuthorization().WithIdempotency();

// La torre también puede reportar: su pestaña Pruebas genera siniestros reales para validar el despacho.
claims.MapPost("/", [Idempotent] async (ReportClaimRequest request, HttpContext http, UserContext user, ClaimsDbContext db, IPublishEndpoint publish, CancellationToken ct) =>
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
    return Results.Created($"/api/v1/claims/{claim.Id}", ClaimResponse.From(claim));
}).RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.Insured},{Roles.ControlTower},{Roles.Admin}" });

claims.MapGet("/", async (UserContext user, ClaimsDbContext db, CancellationToken ct) =>
{
    var query = db.Claims.AsNoTracking();
    if (!user.IsInRole(Roles.ControlTower) && !user.IsInRole(Roles.Admin))
    {
        var adjusterId = user.AdjusterId;
        query = user.IsInRole(Roles.Adjuster)
            ? query.Where(x => adjusterId != null && x.AssignedAdjusterId == adjusterId)
            : query.Where(x => x.InsuredId == user.UserId);
    }
    return Results.Ok(await query.OrderByDescending(x => x.ReportedAt).Select(x => ClaimResponse.From(x)).ToListAsync(ct));
});

claims.MapGet("/{claimId:guid}", async (Guid claimId, UserContext user, ClaimsDbContext db, CancellationToken ct) =>
{
    var claim = await db.Claims.AsNoTracking().SingleOrDefaultAsync(x => x.Id == claimId, ct);
    return claim is null || !CanSee(claim, user) ? Results.NotFound() : Results.Ok(ClaimResponse.From(claim));
});

MapCommand("/{claimId:guid}/cancel", [Roles.Insured, Roles.ControlTower, Roles.Admin], (claim, user, now) =>
{
    var reason = user.IsInRole(Roles.Insured) ? "Cancelled by insured." : "Cancelled by control tower.";
    claim.Cancel(reason, now);
    ClaimsTelemetry.Cancelled.Add(1);
    return new ClaimCancelled(claim.Id, reason, now);
});
MapCommand("/{claimId:guid}/adjuster-arrived", [Roles.Adjuster], (claim, user, now) =>
{
    claim.Arrive(RequiredAdjuster(user), now);
    return new AdjusterArrived(claim.Id, claim.AssignedAdjusterId!.Value, now);
});
MapCommand("/{claimId:guid}/service-started", [Roles.Adjuster], (claim, user, now) =>
{
    claim.Start(RequiredAdjuster(user), now);
    return new AdjusterServiceStarted(claim.Id, claim.AssignedAdjusterId!.Value, now);
});
MapCommand("/{claimId:guid}/service-completed", [Roles.Adjuster], (claim, user, now) =>
{
    claim.Complete(RequiredAdjuster(user), now);
    ClaimsTelemetry.Closed.Add(1);
    return new AdjusterServiceCompleted(claim.Id, claim.AssignedAdjusterId!.Value, now);
});

app.Run();

void MapCommand<TEvent>(string route, string[] roles, Func<Claim, UserContext, DateTimeOffset, TEvent> transition) where TEvent : class
{
    claims.MapPost(route, [Idempotent] async (Guid claimId, HttpContext http, UserContext user, ClaimsDbContext db, IPublishEndpoint publish, CancellationToken ct) =>
    {
        var claim = await db.Claims.Include(x => x.Timeline).SingleOrDefaultAsync(x => x.Id == claimId, ct);
        if (claim is null || !CanSee(claim, user)) return Results.NotFound();
        try
        {
            var previousStatus = claim.Status;
            var @event = transition(claim, user, DateTimeOffset.UtcNow);
            var correlationId = Correlation.From(http);
            await publish.PublishCorrelated(@event, correlationId, ct);
            await publish.PublishCorrelated(new ClaimStatusChanged(claim.Id, previousStatus.ToString(), claim.Status.ToString(), claim.Version, claim.UpdatedAt), correlationId, ct);
            if (claim.Status == ClaimStatus.Closed) await publish.PublishCorrelated(new ClaimClosed(claim.Id, claim.UpdatedAt), correlationId, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ClaimResponse.From(claim));
        }
        catch (ClaimStateException exception) { return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Invalid claim transition", detail: exception.Message); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The claim was modified concurrently. Retry the command."); }
    }).RequireAuthorization(new AuthorizeAttribute { Roles = string.Join(',', roles) });
}

static bool CanSee(Claim claim, UserContext user)
{
    if (user.IsInRole(Roles.ControlTower) || user.IsInRole(Roles.Admin)) return true;
    if (user.IsInRole(Roles.Adjuster)) return user.AdjusterId is not null && claim.AssignedAdjusterId == user.AdjusterId;
    return claim.InsuredId == user.UserId;
}

// Un ajustador sin adjuster_id en el token no puede actuar sobre ningún siniestro.
static Guid RequiredAdjuster(UserContext user) => user.AdjusterId ?? throw new UnauthorizedAccessException("The token has no adjuster_id claim.");

public partial class Program;
