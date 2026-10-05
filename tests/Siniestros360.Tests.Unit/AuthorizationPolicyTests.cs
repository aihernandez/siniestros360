using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Siniestros360.Contracts.Common;
using ContractClaimTypes = Siniestros360.Contracts.Common.ClaimTypes;

namespace Siniestros360.Tests.Unit;

// Las reglas de acceso de todos los servicios viven en dos políticas de ServiceDefaults; aquí se prueban una vez.
public sealed class AuthorizationPolicyTests
{
    private static readonly Guid Assigned = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly IAuthorizationService _authorization = new ServiceCollection().AddLogging().AddSiniestrosAuthorization()
        .BuildServiceProvider().GetRequiredService<IAuthorizationService>();

    [Theory]
    [InlineData(Roles.ControlTower)]
    [InlineData(Roles.Admin)]
    public async Task The_control_tower_participates_in_every_claim(string role)
        => (await CanAccessClaim(User(role, "tower-1"), new ClaimParticipants("someone-else", Other))).Should().BeTrue();

    [Fact]
    public async Task An_adjuster_participates_only_in_the_claim_assigned_to_them()
    {
        (await CanAccessClaim(User(Roles.Adjuster, "adj-1", Assigned), new ClaimParticipants("insured-1", Assigned))).Should().BeTrue();
        (await CanAccessClaim(User(Roles.Adjuster, "adj-1", Assigned), new ClaimParticipants("insured-1", Other))).Should().BeFalse();
        (await CanAccessClaim(User(Roles.Adjuster, "adj-1", Assigned), new ClaimParticipants("insured-1", null))).Should().BeFalse();
    }

    [Fact]
    public async Task An_adjuster_without_adjuster_id_in_the_token_participates_in_nothing()
        => (await CanAccessClaim(User(Roles.Adjuster, "adj-1"), new ClaimParticipants("insured-1", null))).Should().BeFalse();

    [Fact]
    public async Task An_insured_participates_only_in_their_own_claims()
    {
        (await CanAccessClaim(User(Roles.Insured, "insured-1"), new ClaimParticipants("insured-1", Assigned))).Should().BeTrue();
        (await CanAccessClaim(User(Roles.Insured, "insured-1"), new ClaimParticipants("insured-2", Assigned))).Should().BeFalse();
    }

    [Fact]
    public async Task An_adjuster_acts_only_as_themselves_and_the_tower_as_anyone()
    {
        (await CanActAs(User(Roles.Adjuster, "adj-1", Assigned), Assigned)).Should().BeTrue();
        (await CanActAs(User(Roles.Adjuster, "adj-1", Assigned), Other)).Should().BeFalse();
        (await CanActAs(User(Roles.Insured, "insured-1"), Assigned)).Should().BeFalse();
        (await CanActAs(User(Roles.ControlTower, "tower-1"), Other)).Should().BeTrue();
    }

    private async Task<bool> CanAccessClaim(ClaimsPrincipal user, ClaimParticipants claim)
        => (await _authorization.AuthorizeAsync(user, claim, Policies.ClaimParticipant)).Succeeded;

    private async Task<bool> CanActAs(ClaimsPrincipal user, Guid adjusterId)
        => (await _authorization.AuthorizeAsync(user, new AdjusterResource(adjusterId), Policies.AdjusterSelf)).Succeeded;

    private static ClaimsPrincipal User(string role, string subject, Guid? adjusterId = null)
    {
        var claims = new List<Claim> { new(System.Security.Claims.ClaimTypes.NameIdentifier, subject), new(System.Security.Claims.ClaimTypes.Role, role) };
        if (adjusterId is not null) claims.Add(new Claim(ContractClaimTypes.AdjusterId, adjusterId.Value.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
