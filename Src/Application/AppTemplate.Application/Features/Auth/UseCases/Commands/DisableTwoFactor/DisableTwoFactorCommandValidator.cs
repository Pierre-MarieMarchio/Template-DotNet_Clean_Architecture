using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.DisableTwoFactor;

public sealed class DisableTwoFactorCommandValidator : AbstractValidator<DisableTwoFactorCommand>
{
    public DisableTwoFactorCommandValidator() =>
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required.");
}
