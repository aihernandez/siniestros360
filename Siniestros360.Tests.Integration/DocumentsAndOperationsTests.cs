extern alias documents;
extern alias operations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Events;
using DocumentsDbContext = documents::Siniestros360.DocumentsService.Infrastructure.DocumentsDbContext;
using DocumentsProgram = documents::Program;
using OperationsDbContext = operations::Siniestros360.OperationsService.Infrastructure.OperationsDbContext;
using OperationsProgram = operations::Program;

namespace Siniestros360.Tests.Integration;

public sealed class DocumentsFactory() : ServiceFactory<DocumentsProgram>("documentsdb");
public sealed class OperationsFactory() : ServiceFactory<OperationsProgram>("operationsdb");

public sealed class DocumentsApiTests(DocumentsFactory factory) : IClassFixture<DocumentsFactory>
{
    [Fact]
    public async Task Documents_are_visible_only_to_the_claim_owner()
    {
        var claimId = await KnownClaim("insured-docs");
        var owner = factory.ClientFor("insured-docs", "insured");

        using var upload = await owner.SendAsync(Upload(claimId, "application/pdf", [1, 2, 3]));
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/documents/claims/{claimId}")).GetArrayLength().Should().Be(1);

        var stranger = factory.ClientFor("insured-other", "insured");
        (await stranger.GetAsync($"/api/v1/documents/claims/{claimId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var documentId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await stranger.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Empty_or_unsupported_files_are_rejected()
    {
        var claimId = await KnownClaim("insured-bad");
        var owner = factory.ClientFor("insured-bad", "insured");

        (await owner.SendAsync(Upload(claimId, "application/pdf", []))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.SendAsync(Upload(claimId, "application/x-msdownload", [1]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> KnownClaim(string insured)
    {
        var claimId = Guid.NewGuid();
        await factory.Deliver(new ClaimReported(claimId, insured, "SIN-DOCS", "POL-1", "ABC-123", "Collision", 25.68m, -100.31m, false, DateTimeOffset.UtcNow));
        await Eventually.Until(() => factory.Query<DocumentsDbContext, bool>(db => db.ClaimAccess.AnyAsync(x => x.ClaimId == claimId)), x => x);
        return claimId;
    }

    private static HttpRequestMessage Upload(Guid claimId, string contentType, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/documents/claims/{claimId}") { Content = new MultipartFormDataContent { { file, "file", "evidencia.pdf" } } };
        request.Headers.Add("Idempotency-Key", Requests.NewKey());
        return request;
    }
}

public sealed class OperationsApiTests(OperationsFactory factory) : IClassFixture<OperationsFactory>
{
    [Fact]
    public async Task A_redelivered_event_does_not_duplicate_the_alert()
    {
        var claimId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var alert = new NoAdjusterAvailable(claimId, "No available adjuster with recent GPS.", DateTimeOffset.UtcNow);
        await factory.Deliver(alert, messageId);
        await factory.Deliver(alert, messageId);
        await Eventually.Until(() => factory.Query<OperationsDbContext, int>(db => db.Alerts.CountAsync(x => x.ClaimId == claimId)), x => x > 0);
        await Task.Delay(1000);

        var alerts = await factory.ClientFor("tower-1", "control_tower").GetFromJsonAsync<JsonElement>("/api/v1/operations/alerts");
        alerts.EnumerateArray().Count(x => x.GetProperty("claimId").GetGuid() == claimId).Should().Be(1);
    }

    [Fact]
    public async Task Out_of_order_status_does_not_move_the_claim_backwards()
    {
        var claimId = Guid.NewGuid();
        await factory.Deliver(new ClaimReported(claimId, "insured-order", "SIN-ORDER", "POL-1", "ABC-123", "Collision", 25.68m, -100.31m, true, DateTimeOffset.UtcNow));
        await Eventually.Until(() => factory.Query<OperationsDbContext, bool>(db => db.Claims.AnyAsync(x => x.ClaimId == claimId && x.Folio == "SIN-ORDER")), x => x);
        await factory.Deliver(new ClaimStatusChanged(claimId, "AdjusterArrived", "InProgress", 5, DateTimeOffset.UtcNow));
        await Eventually.Until(() => factory.Query<OperationsDbContext, string>(db => db.Claims.Where(x => x.ClaimId == claimId).Select(x => x.Status).SingleAsync()), x => x == "InProgress");
        await factory.Deliver(new ClaimStatusChanged(claimId, "Assigned", "AdjusterArrived", 4, DateTimeOffset.UtcNow));
        await Task.Delay(1000);

        var claim = await factory.ClientFor("tower-1", "control_tower").GetFromJsonAsync<JsonElement>($"/api/v1/operations/claims/{claimId}");
        claim.GetProperty("status").GetString().Should().Be("InProgress");
        claim.GetProperty("requiresAmbulance").GetBoolean().Should().BeTrue();
    }

    // Inicio y cierre seguidos publican InService y Available casi juntos; si llegan al revés, la vista no debe quedar InService.
    [Fact]
    public async Task An_older_adjuster_status_does_not_overwrite_a_newer_one()
    {
        var adjusterId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        await factory.Deliver(new AdjusterStatusChanged(adjusterId, "InService", "Available", started.AddMilliseconds(50)));
        await Eventually.Until(() => factory.Query<OperationsDbContext, string?>(db => db.Adjusters.Where(x => x.AdjusterId == adjusterId).Select(x => x.Status).SingleOrDefaultAsync()), x => x == "Available");
        await factory.Deliver(new AdjusterStatusChanged(adjusterId, "Arrived", "InService", started));
        await Task.Delay(1000);

        var status = await factory.Query<OperationsDbContext, string>(db => db.Adjusters.Where(x => x.AdjusterId == adjusterId).Select(x => x.Status).SingleAsync());
        status.Should().Be("Available");
    }

    [Fact]
    public async Task A_restarted_device_with_a_lower_sequence_still_moves_the_adjuster()
    {
        var adjusterId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        await factory.Deliver(new AdjusterLocationUpdated(adjusterId, 25.60m, -100.30m, 10, 0, 500, before));
        await Eventually.Until(() => factory.Query<OperationsDbContext, bool>(db => db.Adjusters.AnyAsync(x => x.AdjusterId == adjusterId)), x => x);

        await factory.Deliver(new AdjusterLocationUpdated(adjusterId, 25.70m, -100.40m, 10, 0, 1, DateTimeOffset.UtcNow));

        var latitude = await Eventually.Until(() => factory.Query<OperationsDbContext, decimal?>(db => db.Adjusters.Where(x => x.AdjusterId == adjusterId).Select(x => x.Latitude).SingleAsync()), x => x == 25.70m);
        latitude.Should().Be(25.70m, "the capture time orders positions; the sequence restarts with the device");
    }

    [Fact]
    public async Task Operations_views_are_restricted_to_the_control_tower()
    {
        (await factory.ClientFor("insured-x", "insured").GetAsync("/api/v1/operations/claims")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/api/v1/operations/claims")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
