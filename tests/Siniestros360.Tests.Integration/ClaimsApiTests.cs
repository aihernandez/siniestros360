extern alias claims;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siniestros360.Contracts.Events;
using ClaimsDbContext = claims::Siniestros360.ClaimsService.Infrastructure.ClaimsDbContext;
using ClaimsProgram = claims::Program;
using ReportClaimRequest = claims::Siniestros360.ClaimsService.Endpoints.Claims.ReportClaimRequest;

namespace Siniestros360.Tests.Integration;

public sealed class ClaimsFactory : ServiceFactory<ClaimsProgram>
{
    public FailBeforeCommit Fault { get; } = new();

    public ClaimsFactory() : base("claimsdb") { }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.ConfigureDbContext<ClaimsDbContext>(options => options.AddInterceptors(Fault)));
    }
}

public sealed class ClaimsApiTests(ClaimsFactory factory) : IClassFixture<ClaimsFactory>
{
    [Fact]
    public async Task Reporting_requires_authentication()
    {
        var response = await factory.CreateClient().SendAsync(Requests.Post("/api/v1/claims", Report("POL-AUTH"), Requests.NewKey()));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task Reporting_requires_a_guid_idempotency_key(string? key)
    {
        var response = await factory.ClientFor("insured-key", "insured").SendAsync(Requests.Post("/api/v1/claims", Report("POL-KEY"), key));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Idempotency_replays_the_same_request_and_rejects_a_different_payload()
    {
        var client = factory.ClientFor("insured-replay", "insured");
        var key = Requests.NewKey();

        using var first = await client.SendAsync(Requests.Post("/api/v1/claims", Report("POL-REPLAY"), key));
        using var replay = await client.SendAsync(Requests.Post("/api/v1/claims", Report("POL-REPLAY"), key));
        using var mismatch = await client.SendAsync(Requests.Post("/api/v1/claims", Report("POL-OTHER"), key));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.Headers.GetValues("Idempotent-Replayed").Should().ContainSingle("true");
        (await Id(replay)).Should().Be(await Id(first));
        mismatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Concurrent_twins_with_the_same_key_create_a_single_claim()
    {
        var client = factory.ClientFor("insured-twins", "insured");
        var key = Requests.NewKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.SendAsync(Requests.Post("/api/v1/claims", Report("POL-TWINS"), key))));

        responses.Select(x => x.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.Created);
        (await Id(responses[0])).Should().Be(await Id(responses[1]));
        (await factory.Query<ClaimsDbContext, int>(db => db.Claims.CountAsync(x => x.PolicyNumber == "POL-TWINS"))).Should().Be(1);
    }

    [Fact]
    public async Task The_same_key_from_two_users_is_isolated()
    {
        var key = Requests.NewKey();
        using var first = await factory.ClientFor("insured-x1", "insured").SendAsync(Requests.Post("/api/v1/claims", Report("POL-SHARED"), key));
        using var second = await factory.ClientFor("insured-x2", "insured").SendAsync(Requests.Post("/api/v1/claims", Report("POL-SHARED"), key));

        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        (await Id(second)).Should().NotBe(await Id(first));
    }

    [Fact]
    public async Task A_failure_before_commit_rolls_back_claim_outbox_and_key()
    {
        var client = factory.ClientFor("insured-rollback", "insured");
        var key = Requests.NewKey();
        var outboxBefore = await factory.Query<ClaimsDbContext, int>(db => db.Set<OutboxMessage>().CountAsync());

        factory.Fault.Enabled = true;
        using var failed = await client.SendAsync(Requests.Post("/api/v1/claims", Report("POL-ROLLBACK"), key));
        factory.Fault.Enabled = false;

        failed.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await factory.Query<ClaimsDbContext, int>(db => db.Claims.CountAsync(x => x.PolicyNumber == "POL-ROLLBACK"))).Should().Be(0);
        (await factory.Query<ClaimsDbContext, int>(db => db.Set<OutboxMessage>().CountAsync())).Should().Be(outboxBefore);
        (await factory.Query<ClaimsDbContext, int>(db => db.IdempotencyRecords.CountAsync(x => x.Key == key))).Should().Be(0);

        using var retried = await client.SendAsync(Requests.Post("/api/v1/claims", Report("POL-ROLLBACK"), key));
        retried.StatusCode.Should().Be(HttpStatusCode.Created);
        (await factory.Query<ClaimsDbContext, int>(db => db.Claims.CountAsync(x => x.PolicyNumber == "POL-ROLLBACK"))).Should().Be(1);
    }

    [Fact]
    public async Task An_insured_cannot_see_or_cancel_another_insureds_claim()
    {
        var claimId = await Create("insured-owner");
        var stranger = factory.ClientFor("insured-stranger", "insured");

        (await stranger.GetAsync($"/api/v1/claims/{claimId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.SendAsync(Requests.Post($"/api/v1/claims/{claimId}/cancel", idempotencyKey: Requests.NewKey()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_rejected_command_releases_its_key()
    {
        var claimId = await Create("insured-reject");
        var adjusterId = Guid.NewGuid();
        await factory.Deliver(new AdjusterAssigned(claimId, adjusterId, 1.2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5)));
        await Eventually.Until(() => factory.Query<ClaimsDbContext, Guid?>(db => db.Claims.Where(x => x.Id == claimId).Select(x => x.AssignedAdjusterId).SingleAsync()), x => x == adjusterId);
        var key = Requests.NewKey();

        using var rejected = await factory.ClientFor("adjuster-reject", "adjuster", adjusterId).SendAsync(Requests.Post($"/api/v1/claims/{claimId}/service-started", idempotencyKey: key));

        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await factory.Query<ClaimsDbContext, int>(db => db.IdempotencyRecords.CountAsync(x => x.Key == key))).Should().Be(0);
    }

    [Fact]
    public async Task Only_the_assigned_adjuster_advances_the_claim_and_a_redelivered_assignment_is_ignored()
    {
        var claimId = await Create("insured-flow");
        var assigned = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var assignment = new AdjusterAssigned(claimId, assigned, 1.2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
        await factory.Deliver(assignment, messageId);
        await factory.Deliver(assignment, messageId);
        await Eventually.Until(() => factory.Query<ClaimsDbContext, Guid?>(db => db.Claims.Where(x => x.Id == claimId).Select(x => x.AssignedAdjusterId).SingleAsync()), x => x == assigned);
        await Task.Delay(500);

        var other = factory.ClientFor("adjuster-other", "adjuster", Guid.NewGuid());
        (await other.SendAsync(Requests.Post($"/api/v1/claims/{claimId}/adjuster-arrived", idempotencyKey: Requests.NewKey()))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var arrived = await factory.ClientFor("adjuster-assigned", "adjuster", assigned).SendAsync(Requests.Post($"/api/v1/claims/{claimId}/adjuster-arrived", idempotencyKey: Requests.NewKey()));
        arrived.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await arrived.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetString().Should().Be("AdjusterArrived");
        body.RootElement.GetProperty("version").GetInt64().Should().Be(3, "the redelivered assignment must not add a second transition");
    }

    private async Task<Guid> Create(string insured)
    {
        using var response = await factory.ClientFor(insured, "insured").SendAsync(Requests.Post("/api/v1/claims", Report("POL-" + insured), Requests.NewKey()));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await Id(response);
    }

    private static async Task<Guid> Id(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    // AddValidation() sólo descubre los tipos del ensamblado donde se llama: si se llama en ServiceDefaults, los
    // DataAnnotations de ReportClaimRequest (en ClaimsService) no se aplican y el siniestro se crea igual.
    [Theory]
    [InlineData(200, -100.3161)]
    [InlineData(25.6866, -500)]
    public async Task A_report_with_invalid_coordinates_is_rejected_with_400(double latitude, double longitude)
    {
        var report = Report("POL-INVALID") with { Latitude = (decimal)latitude, Longitude = (decimal)longitude };
        using var response = await factory.ClientFor("insured-invalid", "insured").SendAsync(Requests.Post("/api/v1/claims", report, Requests.NewKey()));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static ReportClaimRequest Report(string policy) => new(policy, "ABC-123", "Collision", 25.6866m, -100.3161m, false);
}
