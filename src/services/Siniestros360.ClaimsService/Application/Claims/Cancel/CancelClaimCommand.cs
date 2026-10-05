using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.Cancel;

// El asegurado cancela el suyo; la torre, cualquiera. Libera al ajustador por eventos.
public sealed record CancelClaimCommand(Guid ClaimId) : ICommand<ClaimResponse>;
