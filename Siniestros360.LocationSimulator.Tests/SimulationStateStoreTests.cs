using Microsoft.Extensions.Options;
using Siniestros360.LocationSimulator.Options;
using Siniestros360.LocationSimulator.Services;

namespace Siniestros360.LocationSimulator.Tests;

public sealed class SimulationStateStoreTests
{
    [Fact]
    public void CreatesConfiguredNumberOfAdjusters()
    {
        var snapshots = CreateStore(30).GetSnapshots();
        Assert.Equal(30, snapshots.Count);
        Assert.Equal(30, snapshots.Select(item => item.AdjusterId).Distinct().Count());
    }

    [Fact]
    public void UsesDemoAdjusterIdsFromAdjustersService()
    {
        var snapshots = CreateStore(3).GetSnapshots();
        Assert.Equal("11111111-1111-1111-1111-111111111111", snapshots[0].AdjusterId);
        Assert.Equal("33333333-3333-3333-3333-333333333333", snapshots[2].AdjusterId);
    }

    [Fact]
    public void Advance_ChangesLocationAndSequence()
    {
        var store = CreateStore();
        var before = store.GetSnapshots()[0];
        var batch = store.Advance(TimeSpan.FromSeconds(5));
        var after = store.GetSnapshots()[0];

        Assert.NotNull(batch);
        Assert.NotEqual((before.Latitude, before.Longitude), (after.Latitude, after.Longitude));
        Assert.Equal(before.Sequence + 1, after.Sequence);
    }

    [Fact]
    public void Advance_DoesNothingWhilePaused()
    {
        var store = CreateStore();
        var before = store.GetSnapshots()[0];
        store.Pause();
        var batch = store.Advance(TimeSpan.FromSeconds(5));

        Assert.Null(batch);
        Assert.Equal(before, store.GetSnapshots()[0]);
    }

    [Fact]
    public void Reset_RecreatesInitialPosition()
    {
        var store = CreateStore();
        var initial = store.GetSnapshots()[0];
        store.Advance(TimeSpan.FromSeconds(20));
        store.Reset();
        var reset = store.GetSnapshots()[0];

        Assert.Equal(initial.Latitude, reset.Latitude);
        Assert.Equal(initial.Longitude, reset.Longitude);
        Assert.Equal(0, reset.Sequence);
    }

    private static SimulationStateStore CreateStore(int adjusterCount = 3) => new(
        Microsoft.Extensions.Options.Options.Create(new SimulatorOptions
        {
            AdjusterCount = adjusterCount,
            RandomSeed = 360
        }),
        new RouteCatalog(),
        TimeProvider.System);
}
