extern alias dispatch;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using DispatchDbContext = dispatch::Siniestros360.DispatchService.Infrastructure.DispatchDbContext;
using DispatchProgram = dispatch::Program;

namespace Siniestros360.Tests.Integration;

public sealed class DispatchFactory() : ServiceFactory<DispatchProgram>("dispatchdb");

// La saga real: repositorio EF con concurrencia optimista (xmin), consumer outbox e inbox sobre PostgreSQL.
// Cada prueba usa su propio host y base: un siniestro que quedó esperando en otra prueba tomaría al primer ajustador libre.
public sealed class DispatchSagaTests : IAsyncLifetime
{
    private readonly DispatchFactory factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task Assignment_and_cancellation_release_the_adjuster()
    {
        var adjusterId = await AvailableAdjuster(25.6870m, -100.3165m);
        var claimId = Guid.NewGuid();

        await factory.Deliver(Reported(claimId, 25.6866m, -100.3161m));
        var assigned = await Saga(claimId, status => status == "Assigned");
        assigned.GetProperty("adjusterId").GetGuid().Should().Be(adjusterId);

        await factory.Deliver(new ClaimCancelled(claimId, "Cancelled by insured.", DateTimeOffset.UtcNow));
        var cancelled = await Saga(claimId, status => status == "Cancelled");
        cancelled.GetProperty("attempts").EnumerateArray().Select(x => x.GetProperty("result").GetString()).Should().Contain(["Assigned", "Released"]);
    }

    [Fact]
    public async Task A_waiting_claim_is_assigned_when_an_adjuster_reports_gps()
    {
        var claimId = Guid.NewGuid();
        await factory.Deliver(Reported(claimId, 19.4326m, -99.1332m));
        await Saga(claimId, status => status == "Unavailable");

        await AvailableAdjuster(19.4330m, -99.1335m);

        (await Saga(claimId, status => status == "Assigned")).GetProperty("status").GetString().Should().Be("Assigned");
    }

    [Fact]
    public async Task Arrival_sla_breach_reassigns_and_compensates()
    {
        var first = await AvailableAdjuster(20.6597m, -103.3496m);
        var claimId = Guid.NewGuid();
        await factory.Deliver(Reported(claimId, 20.6597m, -103.3496m));
        await Saga(claimId, status => status == "Assigned");
        var second = await AvailableAdjuster(20.6600m, -103.3500m);

        await factory.Deliver(new SlaBreached(claimId, "WaitingArrival", "Arrival SLA exceeded.", DateTimeOffset.UtcNow));

        var reassigned = await Saga(claimId, _ => true, saga => saga.GetProperty("adjusterId").GetGuid() == second);
        reassigned.GetProperty("previousAdjusterId").GetGuid().Should().Be(first);
        reassigned.GetProperty("attempts").EnumerateArray().Select(x => x.GetProperty("result").GetString()).Should().Contain(["Released", "Reassigned"]);
    }

    [Fact]
    public async Task Manual_reassignment_is_rejected_when_the_claim_is_not_assigned()
    {
        var claimId = Guid.NewGuid();
        await factory.Deliver(Reported(claimId, 32.5149m, -117.0382m));
        await Saga(claimId, status => status == "Unavailable");

        var response = await factory.ClientFor("tower-1", "control_tower").SendAsync(Requests.Post($"/api/v1/dispatch/claims/{claimId}/reassign", new { reason = "test" }, Requests.NewKey()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task<Guid> AvailableAdjuster(decimal latitude, decimal longitude)
    {
        var adjusterId = Guid.NewGuid();
        await factory.Deliver(new AdjusterRegistered(adjusterId, "Ajustador prueba", DateTimeOffset.UtcNow));
        await factory.Deliver(new AdjusterAvailabilityChanged(adjusterId, true, DateTimeOffset.UtcNow));
        await Task.Delay(300);
        await factory.Deliver(new AdjusterLocationUpdated(adjusterId, latitude, longitude, 10, 0, 1, DateTimeOffset.UtcNow));
        // En operación el GPS llega cada segundo y cualquier carrera con un siniestro nuevo se resuelve en la siguiente
        // actualización; aquí hay un solo GPS, así que la prueba espera a que la proyección sea elegible.
        await Eventually.Until(() => factory.Query<DispatchDbContext, bool>(db => db.Adjusters.AnyAsync(x => x.AdjusterId == adjusterId && x.IsAvailable && x.LastLocationAt != null)), x => x);
        return adjusterId;
    }

    private async Task<JsonElement> Saga(Guid claimId, Func<string, bool> status, Func<JsonElement, bool>? extra = null)
    {
        var client = factory.ClientFor("tower-1", "control_tower");
        var last = "";
        var result = await Eventually.Until(async () =>
        {
            var response = await client.GetAsync($"/api/v1/dispatch/claims/{claimId}");
            last = $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}";
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<JsonElement>() : default;
        }, saga => saga.ValueKind == JsonValueKind.Object && status(saga.GetProperty("status").GetString()!) && (extra?.Invoke(saga) ?? true));
        result.ValueKind.Should().Be(JsonValueKind.Object, $"the saga should reach the expected state; last response: {last}");
        status(result.GetProperty("status").GetString()!).Should().BeTrue();
        return result;
    }

    private static ClaimReported Reported(Guid claimId, decimal latitude, decimal longitude) => new(claimId, "insured-a", $"SIN-{claimId:N}"[..12], "POL-1", "ABC-123", "Collision", latitude, longitude, false, DateTimeOffset.UtcNow);
}
