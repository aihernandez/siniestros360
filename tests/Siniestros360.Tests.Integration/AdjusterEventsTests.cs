extern alias adjusters;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using AdjustersDbContext = adjusters::Siniestros360.AdjustersService.Infrastructure.AdjustersDbContext;
using AdjustersProgram = adjusters::Program;

namespace Siniestros360.Tests.Integration;

public sealed class AdjustersFactory() : ServiceFactory<AdjustersProgram>("adjustersdb");

// Adjusters recibe la asignación de Dispatch y el avance del siniestro de Claims por topics distintos: nada garantiza el
// orden, y tras arrancar suele ir atrasado. Cada prueba usa un ajustador sembrado distinto para no interferir.
public sealed class AdjusterEventsTests(AdjustersFactory factory) : IClassFixture<AdjustersFactory>
{
    [Fact]
    public async Task A_late_assignment_for_a_cancelled_claim_does_not_tie_up_the_adjuster()
    {
        var adjusterId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var claimId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await Eventually.Until(() => Status(adjusterId), x => x == "Available");

        await factory.Deliver(new ClaimCancelled(claimId, "Cancelado por la torre de control.", now.AddSeconds(5)));
        await Eventually.Until(() => factory.Query<AdjustersDbContext, bool>(db => db.ClaimRecords.AnyAsync(x => x.ClaimId == claimId && x.FinishedAt != null)), x => x);
        await factory.Deliver(new AdjusterAssigned(claimId, adjusterId, 0.1, now, now.AddMinutes(15)));
        await Task.Delay(1000);

        (await Status(adjusterId)).Should().Be("Available");
        (await factory.Query<AdjustersDbContext, Guid?>(db => db.Adjusters.Where(x => x.Id == adjusterId).Select(x => x.ActiveClaimId).SingleAsync())).Should().BeNull();
    }

    [Fact]
    public async Task A_start_processed_after_the_close_does_not_leave_the_adjuster_in_service()
    {
        var adjusterId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var claimId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await factory.Deliver(new AdjusterAssigned(claimId, adjusterId, 0.1, now, now.AddMinutes(15)));
        await Eventually.Until(() => Status(adjusterId), x => x == "Assigned");
        await factory.Deliver(new AdjusterArrived(claimId, adjusterId, now.AddMinutes(1)));
        await Eventually.Until(() => Status(adjusterId), x => x == "Arrived");

        await factory.Deliver(new AdjusterServiceCompleted(claimId, adjusterId, now.AddMinutes(3)));
        await Eventually.Until(() => Status(adjusterId), x => x == "Available");
        await factory.Deliver(new AdjusterServiceStarted(claimId, adjusterId, now.AddMinutes(2)));
        await Task.Delay(1000);

        (await Status(adjusterId)).Should().Be("Available");
    }

    // Asignación y cancelación procesadas al mismo tiempo: ninguna ve la escritura sin confirmar de la otra. Ambas escriben
    // la fila del siniestro, chocan, y el reintento decide con lo confirmado. El ajustador nunca queda atado.
    [Fact]
    public async Task Assignment_and_cancellation_processed_at_the_same_time_never_tie_up_the_adjuster()
    {
        var adjusterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        for (var round = 0; round < 5; round++)
        {
            var claimId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await Task.WhenAll(
                factory.Deliver(new AdjusterAssigned(claimId, adjusterId, 0.1, now, now.AddMinutes(15))),
                factory.Deliver(new ClaimCancelled(claimId, "Cancelado por la torre de control.", now.AddSeconds(1))));
            await Eventually.Until(() => factory.Query<AdjustersDbContext, bool>(db => db.ClaimRecords.AnyAsync(x => x.ClaimId == claimId && x.FinishedAt != null)), x => x);
            var settled = await Eventually.Until(() => Status(adjusterId), x => x == "Available");
            settled.Should().Be("Available", "ronda {0}: la cancelación debe ganarle a la asignación concurrente", round);
        }
    }

    private Task<string> Status(Guid adjusterId)
        => factory.Query<AdjustersDbContext, string>(db => db.Adjusters.Where(x => x.Id == adjusterId).Select(x => x.Status.ToString()).SingleAsync());
}
