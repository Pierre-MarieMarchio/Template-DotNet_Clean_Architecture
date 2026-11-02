using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RequestPasswordReset;

public sealed class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetCommandValidator() =>
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
}
