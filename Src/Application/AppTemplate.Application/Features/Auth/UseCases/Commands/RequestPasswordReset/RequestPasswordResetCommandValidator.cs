using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RequestPasswordReset;

public sealed class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetCommandValidator() =>
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
}
