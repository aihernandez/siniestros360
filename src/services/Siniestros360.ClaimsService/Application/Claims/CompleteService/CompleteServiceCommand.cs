using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.CompleteService;

// El ajustador cierra el servicio; el siniestro queda cerrado.
public sealed record CompleteServiceCommand(Guid ClaimId) : ICommand<ClaimResponse>;
