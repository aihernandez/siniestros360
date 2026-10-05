extern alias adjusters;
extern alias claims;
extern alias dispatch;
extern alias documents;
extern alias gateway;
extern alias identity;
extern alias location;
extern alias operations;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using AdjustersProgram = adjusters::Program;
using ClaimsProgram = claims::Program;
using DispatchProgram = dispatch::Program;
using DocumentsProgram = documents::Program;
using GatewayProgram = gateway::Program;
using IdentityProgram = identity::Program;
using LocationProgram = location::Program;
using OperationsProgram = operations::Program;

namespace Siniestros360.Tests.Integration;

public sealed class ApiVersioningTests
{
    [Fact]
    public async Task Identity_exposes_only_v1()
    {
        await using var factory = CreateFactory<IdentityProgram>();
        await AssertVersionedApi(factory, "/api/v1/auth/me", "/api/v1/auth/me");

        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokens.For("version-test", "control_tower"));
        using HttpResponseMessage response = await client.GetAsync("/api/v1/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("api-supported-versions").Should().Contain("1.0");
    }

    [Fact]
    public async Task Claims_exposes_only_v1()
    {
        await using var factory = new ServiceFactory<ClaimsProgram>("claimsdb");
        await AssertVersionedApi(factory, "/api/v1/claims", "/api/v1/claims");
    }

    [Fact]
    public async Task Adjusters_exposes_only_v1()
    {
        await using var factory = new ServiceFactory<AdjustersProgram>("adjustersdb");
        await AssertVersionedApi(factory, "/api/v1/adjusters", "/api/v1/adjusters");
    }

    [Fact]
    public async Task Location_exposes_only_v1()
    {
        await using var factory = CreateFactory<LocationProgram>();
        await AssertVersionedApi(factory, "/api/v1/locations/adjusters/recent", "/api/v1/locations/adjusters/recent");
    }

    [Fact]
    public async Task Dispatch_exposes_only_v1()
    {
        await using var factory = new ServiceFactory<DispatchProgram>("dispatchdb");
        await AssertVersionedApi(factory, $"/api/v1/dispatch/claims/{Guid.Empty}", "/api/v1/dispatch/claims/");
    }

    [Fact]
    public async Task Documents_exposes_only_v1()
    {
        await using var factory = new ServiceFactory<DocumentsProgram>("documentsdb");
        await AssertVersionedApi(factory, $"/api/v1/documents/{Guid.Empty}", "/api/v1/documents/");
    }

    [Fact]
    public async Task Operations_exposes_only_v1()
    {
        await using var factory = new ServiceFactory<OperationsProgram>("operationsdb");
        await AssertVersionedApi(factory, "/api/v1/operations/claims", "/api/v1/operations/claims");
    }

    [Fact]
    public async Task Gateway_preserves_the_version_when_forwarding()
    {
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapPost("/api/v{version:int}/auth/login", (int version) => Results.Ok(new { version }));
        await upstream.StartAsync();

        string address = upstream.Urls.Single();
        await using var gateway = new WebApplicationFactory<GatewayProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:SigningKey", Tokens.Key);
            builder.UseSetting("ReverseProxy:Clusters:identity:Destinations:primary:Address", address);
        });
        using HttpClient client = gateway.CreateClient();

        string[] routes = gateway.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToArray();
        foreach (string path in new[] { "auth", "claims", "adjusters", "locations", "dispatch", "documents", "operations" })
        {
            routes.Should().Contain($"/api/v{{version:apiVersion}}/{path}/{{**catch-all}}");
        }

        foreach (int version in new[] { 1, 2 })
        {
            using HttpResponseMessage response = await client.PostAsync(
                $"/api/v{version}/auth/login", JsonContent.Create(new { }));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>();
            result.GetProperty("version").GetInt32().Should().Be(version);
        }
    }

    private static async Task AssertVersionedApi<TProgram>(
        WebApplicationFactory<TProgram> factory,
        string requestPath,
        string documentPath) where TProgram : class
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage v1 = await client.GetAsync(requestPath);
        v1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var unsupportedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            requestPath.Replace("/v1/", "/v2/", StringComparison.Ordinal));
        unsupportedRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokens.For("version-test", "control_tower"));
        using HttpResponseMessage v2 = await client.SendAsync(unsupportedRequest);
        v2.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using HttpResponseMessage openApi = await client.GetAsync("/openapi/v1.json");
        openApi.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Select(path => path.Name)
            .Should().Contain(path => path.StartsWith(documentPath, StringComparison.Ordinal));
    }

    private static WebApplicationFactory<TProgram> CreateFactory<TProgram>() where TProgram : class
    {
        return new WebApplicationFactory<TProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:SigningKey", Tokens.Key);
        });
    }
}
