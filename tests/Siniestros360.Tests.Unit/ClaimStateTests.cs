using FluentAssertions;
using Siniestros360.ClaimsService.Domain;

namespace Siniestros360.Tests.Unit;

public sealed class ClaimStateTests
{
    private static readonly Guid AdjusterId = Guid.NewGuid();

    [Fact]
    public void Service_cannot_start_before_arrival()
    {
        var claim = AssignedClaim();
        var action = () => claim.Start(AdjusterId, DateTimeOffset.UtcNow);
        action.Should().Throw<ClaimStateException>().WithMessage("*arrival*");
    }

    [Fact]
    public void Closed_claim_is_immutable()
    {
        var claim = AssignedClaim(); var now = DateTimeOffset.UtcNow;
        claim.Arrive(AdjusterId, now); claim.Start(AdjusterId, now); claim.Complete(AdjusterId, now);
        var action = () => claim.Cancel("late cancellation", now);
        action.Should().Throw<ClaimStateException>();
        claim.Status.Should().Be(ClaimStatus.Closed);
    }

    [Fact]
    public void Escalation_marks_the_claim_without_blocking_the_service()
    {
        var claim = AssignedClaim(); var now = DateTimeOffset.UtcNow;
        claim.Arrive(AdjusterId, now); claim.Start(AdjusterId, now);
        claim.Escalate("SLA InService exceeded 30 minutes.", now);
        claim.Complete(AdjusterId, now);
        claim.Status.Should().Be(ClaimStatus.Closed);
        claim.EscalatedAt.Should().Be(now);
    }

    [Fact]
    public void Only_the_assigned_adjuster_can_arrive()
    {
        var claim = AssignedClaim();
        var action = () => claim.Arrive(Guid.NewGuid(), DateTimeOffset.UtcNow);
        action.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void Late_reassignment_is_ignored_once_the_adjuster_is_on_site()
    {
        var claim = AssignedClaim(); var now = DateTimeOffset.UtcNow;
        claim.Arrive(AdjusterId, now);
        claim.TryAssign(Guid.NewGuid(), now, isReassignment: true).Should().BeFalse();
        claim.AssignedAdjusterId.Should().Be(AdjusterId);
        claim.Status.Should().Be(ClaimStatus.AdjusterArrived);
    }

    private static Claim AssignedClaim()
    {
        var claim = new Claim { Id = Guid.NewGuid(), InsuredId = "insured-a", Folio = "SIN-1", PolicyNumber = "POL-1", VehiclePlate = "ABC-123", IncidentType = "Collision", Status = ClaimStatus.AssignmentPending, ReportedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Version = 1 };
        claim.TryAssign(AdjusterId, DateTimeOffset.UtcNow, isReassignment: false).Should().BeTrue();
        return claim;
    }
}
