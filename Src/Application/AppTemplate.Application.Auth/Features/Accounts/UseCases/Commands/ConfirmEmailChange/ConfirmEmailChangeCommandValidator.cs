using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ConfirmEmailChange;

public sealed class ConfirmEmailChangeCommandValidator : AbstractValidator<ConfirmEmailChangeCommand>
{
    public ConfirmEmailChangeCommandValidator()
    {
        RuleFor(x => x.NewEmail).NotEmpty().WithMessage("Email is required.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Token is required.");
    }
}
