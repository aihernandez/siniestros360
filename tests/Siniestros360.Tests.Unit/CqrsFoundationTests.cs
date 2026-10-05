using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Siniestros360.ServiceDefaults.Endpoints;
using Siniestros360.SharedKernel;
using Siniestros360.SharedKernel.Messaging;

namespace Siniestros360.Tests.Unit;

// La base de CQRS (ADR-008): decoradores registrados por AddApplication y traducción de Error a HTTP.
public sealed class CqrsFoundationTests
{
    [Fact]
    public async Task An_invalid_command_never_reaches_its_handler()
    {
        var handler = Provider().GetRequiredService<ICommandHandler<EchoCommand, string>>();

        var result = await handler.Handle(new EchoCommand(""), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainSingle(error => error.Code == nameof(EchoCommand.Text));
        EchoCommandHandler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_valid_command_runs_through_the_decorators_to_its_handler()
    {
        var handler = Provider().GetRequiredService<ICommandHandler<EchoCommand, string>>();

        var result = await handler.Handle(new EchoCommand("hola"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hola");
    }

    [Theory]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.Problem, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.Failure, StatusCodes.Status500InternalServerError)]
    public void Each_error_type_maps_to_its_http_status(ErrorType type, int status)
    {
        var problem = CustomResults.Problem(Result.Failure(new Error("Test.Code", "Descripción", type))).Should().BeOfType<ProblemHttpResult>().Subject;

        problem.StatusCode.Should().Be(status);
        if (type != ErrorType.Failure) problem.ProblemDetails.Extensions["code"].Should().Be("Test.Code");
    }

    [Fact]
    public void A_validation_error_maps_to_400_with_the_errors_per_field()
    {
        var result = Result.Failure(new ValidationError([Error.Problem("Latitude", "Fuera de rango.")]));

        var problem = CustomResults.Problem(result).Should().BeOfType<ValidationProblem>().Subject;

        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Errors["Latitude"].Should().Equal("Fuera de rango.");
    }

    // Una sola instancia: el handler cuenta llamadas en un campo estático, y las pruebas de esta clase corren en serie.
    private static ServiceProvider Provider()
    {
        EchoCommandHandler.Calls = 0;
        return new ServiceCollection().AddLogging().AddApplication(typeof(CqrsFoundationTests).Assembly).BuildServiceProvider();
    }
}

public sealed record EchoCommand(string Text) : ICommand<string>;

internal sealed class EchoCommandValidator : AbstractValidator<EchoCommand>
{
    public EchoCommandValidator() => RuleFor(command => command.Text).NotEmpty();
}

internal sealed class EchoCommandHandler : ICommandHandler<EchoCommand, string>
{
    public static int Calls { get; set; }

    public Task<Result<string>> Handle(EchoCommand command, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<Result<string>>(command.Text);
    }
}
