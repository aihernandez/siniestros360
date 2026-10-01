using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siniestros360.Contracts.Events;
using Siniestros360.DispatchService.Application;
using Siniestros360.DispatchService.Domain;
using Siniestros360.DispatchService.Infrastructure;

namespace Siniestros360.Tests.Unit;

// Transiciones y eventos de la saga con el test harness de MassTransit (transporte y repositorio en memoria).
// Las reglas de reserva se prueban aparte; aquí se siembran ajustadores antes de publicar.
public sealed class AssignmentStateMachineTests : IAsyncLifetime
{
    private readonly string _database = Guid.NewGuid().ToString();
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;
    private ISagaStateMachineTestHarness<AssignmentStateMachine, AssignmentState> _saga = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<DispatchDbContext>(options => options.UseInMemoryDatabase(_database));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AdjusterReservations>();
        services.AddMassTransitTestHarness(bus => bus.AddSagaStateMachine<AssignmentStateMachine, AssignmentState>().InMemoryRepository());
        _provider = services.BuildServiceProvider(true);
        _harness = _provider.GetTestHarness();
        await _harness.Start();
        _saga = _harness.GetSagaStateMachineHarness<AssignmentStateMachine, AssignmentState>();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Reported_claim_with_an_available_adjuster_is_assigned()
    {
        await SeedAdjuster(25.69m, -100.31m);
        var claimId = Guid.NewGuid();

        await _harness.Bus.Publish(Reported(claimId));

        (await _saga.Exists(claimId, x => x.Assigned)).Should().NotBeNull();
        (await _harness.Published.Any<AdjusterAssigned>(x => x.Context.Message.ClaimId == claimId)).Should().BeTrue();
    }

    [Fact]
    public async Task Without_adjusters_the_claim_waits_and_a_retry_assigns_it()
    {
        var claimId = Guid.NewGuid();
        await _harness.Bus.Publish(Reported(claimId));
        (await _saga.Exists(claimId, x => x.Unavailable)).Should().NotBeNull();
        (await _harness.Published.Any<NoAdjusterAvailable>(x => x.Context.Message.ClaimId == claimId)).Should().BeTrue();

        await SeedAdjuster(25.69m, -100.31m);
        await _harness.Bus.Publish(new RetryAssignment(claimId));

        (await _saga.Exists(claimId, x => x.Assigned)).Should().NotBeNull();
    }

    [Fact]
    public async Task Arrival_sla_breach_reassigns_but_not_once_the_adjuster_is_on_site()
    {
        await SeedAdjuster(25.69m, -100.31m);
        await SeedAdjuster(25.70m, -100.32m);
        var reassigned = Guid.NewGuid();
        var onSite = Guid.NewGuid();
        await _harness.Bus.Publish(Reported(reassigned));
        (await _saga.Exists(reassigned, x => x.Assigned)).Should().NotBeNull();

        await _harness.Bus.Publish(new SlaBreached(reassigned, "WaitingArrival", "Arrival SLA exceeded.", DateTimeOffset.UtcNow));
        (await _harness.Published.Any<ClaimReassigned>(x => x.Context.Message.ClaimId == reassigned)).Should().BeTrue();

        await _harness.Bus.Publish(Reported(onSite));
        (await _saga.Exists(onSite, x => x.Assigned)).Should().NotBeNull();
        await _harness.Bus.Publish(new AdjusterArrived(onSite, Guid.NewGuid(), DateTimeOffset.UtcNow));
        (await _saga.Exists(onSite, x => x.OnSite)).Should().NotBeNull();
        await _harness.Bus.Publish(new SlaBreached(onSite, "WaitingArrival", "late event", DateTimeOffset.UtcNow));
        (await _harness.Consumed.Any<SlaBreached>(x => x.Context.Message.ClaimId == onSite)).Should().BeTrue();
        (await _harness.Published.Any<ClaimReassigned>(x => x.Context.Message.ClaimId == onSite)).Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_finishes_the_saga()
    {
        await SeedAdjuster(25.69m, -100.31m);
        var claimId = Guid.NewGuid();
        await _harness.Bus.Publish(Reported(claimId));
        (await _saga.Exists(claimId, x => x.Assigned)).Should().NotBeNull();

        await _harness.Bus.Publish(new ClaimCancelled(claimId, "Cancelled by insured.", DateTimeOffset.UtcNow));

        (await _saga.Exists(claimId, x => x.Cancelled)).Should().NotBeNull();
    }

    private async Task SeedAdjuster(decimal latitude, decimal longitude)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DispatchDbContext>();
        db.Adjusters.Add(new AdjusterDispatchProjection { AdjusterId = Guid.NewGuid(), IsAvailable = true, Latitude = latitude, Longitude = longitude, LastLocationAt = DateTimeOffset.UtcNow, LastLocationSequence = 1, Version = 1 });
        await db.SaveChangesAsync();
    }

    private static ClaimReported Reported(Guid claimId) => new(claimId, "insured-a", "SIN-1", "POL-1", "ABC-123", "Collision", 25.6866m, -100.3161m, false, DateTimeOffset.UtcNow);
}
