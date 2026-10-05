using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.RegisterArrival;

// El ajustador asignado registra que llegó a la escena.
public sealed record RegisterArrivalCommand(Guid ClaimId) : ICommand<ClaimResponse>;
