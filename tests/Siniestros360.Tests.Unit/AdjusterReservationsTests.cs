using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Siniestros360.DispatchService.Application;
using Siniestros360.DispatchService.Domain;
using Siniestros360.DispatchService.Infrastructure;

namespace Siniestros360.Tests.Unit;

public sealed class AdjusterReservationsTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reserves_the_nearest_available_adjuster_with_recent_gps()
    {
        await using var db = NewDb();
        var near = Adjuster(25.69m, -100.31m);
        db.Adjusters.AddRange(near, Adjuster(25.90m, -100.50m), Adjuster(25.6867m, -100.3162m, available: false), Adjuster(25.6868m, -100.3160m, lastGps: Now.AddMinutes(-10)));
        await db.SaveChangesAsync();
        var saga = NewSaga();

        var reservation = await Reservations(db).TryReserve(saga, default);

        reservation!.AdjusterId.Should().Be(near.AdjusterId);
        near.ReservedForClaimId.Should().Be(saga.CorrelationId);
        saga.AdjusterId.Should().Be(near.AdjusterId);
        db.Attempts.Local.Should().ContainSingle(x => x.OccurredAt == Now);
    }

    [Fact]
    public async Task Without_candidates_nothing_is_reserved()
    {
        await using var db = NewDb();
        var saga = NewSaga();
        (await Reservations(db).TryReserve(saga, default)).Should().BeNull();
        saga.AdjusterId.Should().BeNull();
        db.Attempts.Local.Should().ContainSingle(x => x.Result == "NoAdjusterAvailable");
    }

    [Fact]
    public async Task Reassignment_compensates_by_releasing_the_previous_adjuster()
    {
        await using var db = NewDb();
        var first = Adjuster(25.69m, -100.31m);
        var second = Adjuster(25.70m, -100.32m);
        db.Adjusters.AddRange(first, second);
        await db.SaveChangesAsync();
        var saga = NewSaga();
        var reservations = Reservations(db);
        await reservations.TryReserve(saga, default);

        var reservation = await reservations.TryReserve(saga, default, isReassignment: true);

        reservation!.AdjusterId.Should().Be(second.AdjusterId);
        saga.PreviousAdjusterId.Should().Be(first.AdjusterId);
        first.IsAvailable.Should().BeTrue();
        first.ReservedForClaimId.Should().BeNull();
        db.Attempts.Local.Select(x => x.Result).Should().Contain(["Assigned", "Released", "Reassigned"]);
    }

    [Fact]
    public async Task Release_ignores_an_adjuster_reserved_for_another_claim()
    {
        await using var db = NewDb();
        var adjuster = Adjuster(25.69m, -100.31m);
        adjuster.IsAvailable = false;
        adjuster.ReservedForClaimId = Guid.NewGuid();
        db.Adjusters.Add(adjuster);
        await db.SaveChangesAsync();

        await Reservations(db).Release(adjuster.AdjusterId, Guid.NewGuid(), default);

        adjuster.IsAvailable.Should().BeFalse("a late event must not undo a newer reservation");
    }

    private static DispatchDbContext NewDb() => new(new DbContextOptionsBuilder<DispatchDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static AdjusterReservations Reservations(DispatchDbContext db) => new(db, new ConfigurationBuilder().AddInMemoryCollection().Build(), new FixedDateTimeProvider(Now.UtcDateTime));
    private static AdjusterDispatchProjection Adjuster(decimal latitude, decimal longitude, bool available = true, DateTimeOffset? lastGps = null)
        => new() { AdjusterId = Guid.NewGuid(), IsAvailable = available, Latitude = latitude, Longitude = longitude, LastLocationAt = lastGps ?? Now, LastLocationSequence = 1 };
    private static AssignmentState NewSaga() => new() { CorrelationId = Guid.NewGuid(), CurrentState = "Initial", IncidentLatitude = 25.6866m, IncidentLongitude = -100.3161m, StartedAt = Now, UpdatedAt = Now };
}
