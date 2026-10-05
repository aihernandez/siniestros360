using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Siniestros360.IdentityService.Application;
using Siniestros360.IdentityService.Domain;
using Siniestros360.IdentityService.Endpoints;
using Siniestros360.IdentityService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
// En este ensamblado: el generador de validación sólo descubre los tipos de los endpoints del proyecto que lo llama.
builder.Services.AddValidation();
builder.Services.AddDbContext<IdentityData>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("identitydb");
    if (string.IsNullOrWhiteSpace(connectionString)) options.UseInMemoryDatabase("identity-development");
    else options.UseNpgsql(connectionString);
});
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<IdentityData>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();
app.UseSiniestrosApiDefaults();

using (var scope = app.Services.CreateScope())
{
    await IdentitySeed.InitializeAsync(scope.ServiceProvider);
}

app.MapGroup("/api/v1/auth")
    .WithTags("Authentication")
    .MapAuthEndpoints();

app.Run();

public partial class Program;
