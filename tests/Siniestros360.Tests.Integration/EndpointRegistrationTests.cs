extern alias identity;
extern alias location;

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using IdentityProgram = identity::Program;
using LocationProgram = location::Program;

namespace Siniestros360.Tests.Integration;

public sealed class EndpointRegistrationTests
{
    [Fact]
    public async Task Identity_endpoints_are_registered()
    {
        await using var factory = CreateFactory<IdentityProgram>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Location_endpoints_are_registered()
    {
        await using var factory = CreateFactory<LocationProgram>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/locations/adjusters/recent");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static WebApplicationFactory<TProgram> CreateFactory<TProgram>() where TProgram : class
        => new WebApplicationFactory<TProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:SigningKey", Tokens.Key);
        });
}
