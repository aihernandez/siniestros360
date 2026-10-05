using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.Report;

// El asegurado (o la torre, con siniestros de prueba) reporta desde la escena. Quién reporta sale del token, no del comando.
public sealed record ReportClaimCommand(string PolicyNumber, string VehiclePlate, string IncidentType, decimal Latitude, decimal Longitude, bool RequiresAmbulance)
    : ICommand<ClaimResponse>;
