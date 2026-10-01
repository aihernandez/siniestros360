using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Siniestros360.IdentityService.Application;
using Siniestros360.IdentityService.Domain;
using Siniestros360.IdentityService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddSiniestrosApiDefaults();
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

var auth = app.MapGroup("/api/v1/auth").WithTags("Authentication");
auth.MapPost("/login", async (LoginRequest request, UserManager<ApplicationUser> users, TokenService tokens, CancellationToken ct) =>
{
    var user = await users.FindByEmailAsync(request.Email);
    if (user is null || !await users.CheckPasswordAsync(user, request.Password))
        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials");
    return Results.Ok(await tokens.IssueAsync(user, ct));
}).AllowAnonymous();
auth.MapPost("/refresh", async (RefreshRequest request, TokenService tokens, CancellationToken ct) =>
{
    var result = await tokens.RefreshAsync(request.RefreshToken, ct);
    return result is null
        ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid refresh token")
        : Results.Ok(result);
}).AllowAnonymous();
auth.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
{
    id = user.FindFirstValue(ClaimTypes.NameIdentifier),
    email = user.FindFirstValue(ClaimTypes.Email),
    name = user.FindFirstValue(ClaimTypes.Name),
    adjusterId = user.FindFirstValue("adjuster_id"),
    roles = user.FindAll(ClaimTypes.Role).Select(x => x.Value)
})).RequireAuthorization();

app.Run();
public partial class Program;
