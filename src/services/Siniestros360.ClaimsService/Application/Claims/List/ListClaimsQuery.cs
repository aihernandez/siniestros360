using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.ClaimsService.Application.Claims.List;

public sealed record ListClaimsQuery : IQuery<IReadOnlyList<ClaimResponse>>;
