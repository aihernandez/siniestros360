using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace Siniestros360.LocationSimulator.Tests;

public sealed class ApiVersioningTests
{
    [Fact]
    public async Task Forwarding_without_an_explicit_password_fails_startup()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Simulator:Forwarding:Enabled", "true");
            builder.UseSetting("Simulator:Forwarding:Password", "");
        });

        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Simulator:Forwarding:Password", error.Message);
    }

    [Fact]
    public async Task Simulator_exposes_v1_and_rejects_v2()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Simulator:UpdateIntervalMilliseconds", "300000");
        });
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage v1 = await client.GetAsync("/api/v1/simulator/status");
        Assert.Equal(HttpStatusCode.OK, v1.StatusCode);
        Assert.Contains("1.0", v1.Headers.GetValues("api-supported-versions"));

        using HttpResponseMessage v2 = await client.GetAsync("/api/v2/simulator/status");
        Assert.Equal(HttpStatusCode.NotFound, v2.StatusCode);

        using HttpResponseMessage openApi = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
        Assert.Contains("/api/v1/simulator/status", document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Select(path => path.Name));
    }
}
