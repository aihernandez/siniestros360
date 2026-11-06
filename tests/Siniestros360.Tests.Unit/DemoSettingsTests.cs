using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Siniestros360.IdentityService.Domain;
using Siniestros360.IdentityService.Infrastructure;
using Siniestros360.ServiceDefaults;

namespace Siniestros360.Tests.Unit;

public sealed class DemoSettingsTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Demo_disabled_by_default_does_not_create_users(string environment)
    {
        await using var services = CreateServices(environment);
        var database = services.GetRequiredService<IdentityData>();
        await database.Database.EnsureCreatedAsync();

        await IdentitySeed.InitializeAsync(services);

        Assert.Equal(0, await database.Users.CountAsync());
        Assert.Equal(0, await database.Roles.CountAsync());
    }

    [Fact]
    public async Task Demo_enabled_in_production_rejects_before_creating_accounts()
    {
        await using var services = CreateServices("Production", enabled: true, NewPassword());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => IdentitySeed.InitializeAsync(services));

        Assert.Contains("only in Development", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    public async Task Demo_enabled_requires_an_explicit_password(string? password)
    {
        await using var services = CreateServices("Development", enabled: true, password);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => IdentitySeed.InitializeAsync(services));

        Assert.Contains("Demo:Password", error.Message);
    }

    [Fact]
    public async Task Explicit_development_seed_uses_supplied_password_and_is_idempotent()
    {
        var password = NewPassword();
        await using var services = CreateServices("Development", enabled: true, password);
        var database = services.GetRequiredService<IdentityData>();
        await database.Database.EnsureCreatedAsync();

        await IdentitySeed.InitializeAsync(services);
        await IdentitySeed.InitializeAsync(services);

        Assert.Equal(4, await database.Users.CountAsync());
        Assert.Equal(4, await database.Roles.CountAsync());
        var manager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByEmailAsync("insured.demo@demo.com");
        Assert.NotNull(user);
        Assert.True(await manager.CheckPasswordAsync(user, password));
        Assert.False(await manager.CheckPasswordAsync(user, NewPassword()));
        Assert.True(await manager.IsInRoleAsync(user, "insured"));
    }

    private static string NewPassword() => $"Aa1!{Guid.NewGuid():N}";

    private static ServiceProvider CreateServices(string environment, bool? enabled = null, string? password = null)
    {
        var values = new Dictionary<string, string?> { ["Demo:Password"] = password };
        if (enabled.HasValue)
        {
            values["Demo:Enabled"] = enabled.Value.ToString();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
        services.AddDbContext<IdentityData>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentityData>();
        return services.BuildServiceProvider();
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "DemoSettingsTests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
