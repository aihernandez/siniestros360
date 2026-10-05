using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.StartService;

// El ajustador inicia la atención en sitio.
public sealed record StartServiceCommand(Guid ClaimId) : ICommand<ClaimResponse>;
