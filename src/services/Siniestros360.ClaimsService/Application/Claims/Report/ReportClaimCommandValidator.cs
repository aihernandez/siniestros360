using FluentValidation;

namespace Siniestros360.ClaimsService.Application.Claims.Report;

internal sealed class ReportClaimCommandValidator : AbstractValidator<ReportClaimCommand>
{
    public ReportClaimCommandValidator()
    {
        RuleFor(command => command.PolicyNumber).NotEmpty().Length(3, 40);
        RuleFor(command => command.VehiclePlate).NotEmpty().Length(3, 20);
        RuleFor(command => command.IncidentType).NotEmpty().MaximumLength(40);
        RuleFor(command => command.Latitude).InclusiveBetween(-90, 90);
        RuleFor(command => command.Longitude).InclusiveBetween(-180, 180);
    }
}
