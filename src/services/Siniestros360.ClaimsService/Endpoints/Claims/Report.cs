using Siniestros360.ClaimsService.Application.Claims;
using Siniestros360.ClaimsService.Application.Claims.Report;
using Siniestros360.Contracts.Common;
using Siniestros360.Messaging.Idempotency;
using Siniestros360.ServiceDefaults.Endpoints;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Endpoints.Claims;

/// <summary>Datos del siniestro que envía el asegurado desde la escena.</summary>
public sealed record ReportClaimRequest(string PolicyNumber, string VehiclePlate, string IncidentType, decimal Latitude, decimal Longitude, bool RequiresAmbulance);

internal sealed class Report : IEndpoint
{
    // 24 h de idempotencia: el reporte sale del teléfono en la escena, quizá sin señal, y un reintento horas después no
    // debe duplicar el siniestro.
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/", [Idempotent(expirationMinutes: 24 * 60)] async (
            ReportClaimRequest request,
            ICommandHandler<ReportClaimCommand, ClaimResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new ReportClaimCommand(
                request.PolicyNumber,
                request.VehiclePlate,
                request.IncidentType,
                request.Latitude,
                request.Longitude,
                request.RequiresAmbulance);
            Result<ClaimResponse> result = await handler.Handle(command, cancellationToken);

            return result.Match(claim => Results.Created(ApiVersions.V1Path($"claims/{claim.Id}"), claim), CustomResults.Problem);
        })
        .RequireAuthorization(Policies.ClaimReporters)
        .WithName("ReportClaim")
        .WithSummary("Reportar un siniestro")
        .WithDescription("Responde con el folio de inmediato; la póliza se valida y el ajustador se asigna por eventos.")
        .Produces<ClaimResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();
    }
}
