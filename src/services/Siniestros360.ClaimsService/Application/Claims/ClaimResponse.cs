using Siniestros360.ClaimsService.Domain;

namespace Siniestros360.ClaimsService.Application.Claims;

/// <summary>Siniestro tal como lo ven el asegurado, el ajustador y la torre.</summary>
public sealed record ClaimResponse(Guid Id, string Folio, string PolicyNumber, string VehiclePlate, string IncidentType, decimal Latitude, decimal Longitude, bool RequiresAmbulance, ClaimStatus Status, string CoverageStatus, string? CoverageReason, Guid? AssignedAdjusterId, DateTimeOffset? EscalatedAt, DateTimeOffset ReportedAt, long Version)
{
    public static ClaimResponse From(Claim value) => new(value.Id, value.Folio, value.PolicyNumber, value.VehiclePlate, value.IncidentType, value.Latitude, value.Longitude, value.RequiresAmbulance, value.Status, value.CoverageStatus, value.CoverageReason, value.AssignedAdjusterId, value.EscalatedAt, value.ReportedAt, value.Version);
}
