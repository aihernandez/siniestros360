using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Siniestros360.Contracts.Common;
using Siniestros360.IdentityService.Domain;

namespace Siniestros360.IdentityService.Infrastructure;

public sealed class IdentityData(DbContextOptions<IdentityData> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");
        builder.Entity<RefreshToken>().HasIndex(x => x.TokenHash).IsUnique();
    }
}

public static class IdentitySeed
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<IdentityData>();
        if (db.Database.IsRelational()) await db.Database.MigrateAsync();
        else await db.Database.EnsureCreatedAsync();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var role in new[] { Roles.Insured, Roles.Adjuster, Roles.ControlTower, Roles.Admin })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
            }
        }

        await EnsureUser(userManager, "insured.demo@demo.com", "Asegurado Demo", Roles.Insured, null);
        await EnsureUser(userManager, "adjuster.demo@demo.com", "Ajustador Demo", Roles.Adjuster, Guid.Parse("11111111-1111-1111-1111-111111111111"));
        await EnsureUser(userManager, "tower.demo@demo.com", "Torre de Control Demo", Roles.ControlTower, null);
        await EnsureUser(userManager, "admin.demo@demo.com", "Administrador Demo", Roles.Admin, null);
    }

    private static async Task EnsureUser(UserManager<ApplicationUser> manager, string email, string name, string role, Guid? adjusterId)
    {
        var user = await manager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = name,
                AdjusterId = adjusterId
            };
            var result = await manager.CreateAsync(user, "Demo!2026");
            if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        }

        if (!await manager.IsInRoleAsync(user, role))
        {
            await manager.AddToRoleAsync(user, role);
        }
    }
}
