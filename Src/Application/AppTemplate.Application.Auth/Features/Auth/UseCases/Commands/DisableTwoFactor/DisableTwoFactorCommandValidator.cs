using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DisableTwoFactor;

/// <summary>Presence only: whether the password is right is the hasher's answer, not a validator's.</summary>
public sealed class DisableTwoFactorCommandValidator : AbstractValidator<DisableTwoFactorCommand>
{
    /// <summary>The current password is required.</summary>
    public DisableTwoFactorCommandValidator() =>
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required.");
}
