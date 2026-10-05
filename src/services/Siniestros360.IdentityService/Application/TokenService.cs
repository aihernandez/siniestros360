using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Siniestros360.IdentityService.Domain;
using Siniestros360.IdentityService.Infrastructure;
using ContractClaimTypes = Siniestros360.Contracts.Common.ClaimTypes;

namespace Siniestros360.IdentityService.Application;

public sealed class TokenService(
    IConfiguration configuration,
    IdentityData db,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider)
{
    public async Task<TokenResponse> IssueAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(configuration.GetValue("Jwt:AccessTokenMinutes", 30));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email!),
            new(ClaimTypes.Name, user.DisplayName)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        if (user.AdjusterId is not null) claims.Add(new Claim(ContractClaimTypes.AdjusterId, user.AdjusterId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"]!));
        var token = new JwtSecurityToken(
            configuration["Jwt:Issuer"] ?? "Siniestros360.Identity",
            configuration["Jwt:Audience"] ?? "Siniestros360",
            claims,
            now.UtcDateTime,
            expires.UtcDateTime,
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var refreshPlainText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = Hash(refreshPlainText),
            CreatedAt = now,
            ExpiresAt = now.AddDays(7)
        });
        await db.SaveChangesAsync(cancellationToken);
        return new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token), refreshPlainText, expires, roles.ToArray());
    }

    public async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= timeProvider.GetUtcNow()) return null;
        stored.RevokedAt = timeProvider.GetUtcNow();
        var user = await userManager.FindByIdAsync(stored.UserId.ToString());
        return user is null ? null : await IssueAsync(user, cancellationToken);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string[] Roles);
public sealed record LoginRequest([property: Required] string Email, [property: Required] string Password);
public sealed record RefreshRequest([property: Required] string RefreshToken);
