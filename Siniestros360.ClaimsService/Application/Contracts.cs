using System.ComponentModel.DataAnnotations;
using Siniestros360.ClaimsService.Domain;

namespace Siniestros360.ClaimsService.Application;

public sealed record ReportClaimRequest(
    [property: Required, StringLength(40, MinimumLength = 3)] string PolicyNumber,
    [property: Required, StringLength(20, MinimumLength = 3)] string VehiclePlate,
    [property: Required, StringLength(40)] string IncidentType,
    [property: Range(-90, 90)] decimal Latitude,
    [property: Range(-180, 180)] decimal Longitude,
    bool RequiresAmbulance);

public sealed record ClaimResponse(Guid Id, string Folio, string PolicyNumber, string VehiclePlate, string IncidentType, decimal Latitude, decimal Longitude, bool RequiresAmbulance, ClaimStatus Status, string CoverageStatus, string? CoverageReason, Guid? AssignedAdjusterId, DateTimeOffset? EscalatedAt, DateTimeOffset ReportedAt, long Version)
{
    public static ClaimResponse From(Claim value) => new(value.Id, value.Folio, value.PolicyNumber, value.VehiclePlate, value.IncidentType, value.Latitude, value.Longitude, value.RequiresAmbulance, value.Status, value.CoverageStatus, value.CoverageReason, value.AssignedAdjusterId, value.EscalatedAt, value.ReportedAt, value.Version);
}
