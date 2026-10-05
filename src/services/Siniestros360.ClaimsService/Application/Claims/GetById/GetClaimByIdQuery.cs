using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.GetById;

public sealed record GetClaimByIdQuery(Guid ClaimId) : IQuery<ClaimResponse>;
