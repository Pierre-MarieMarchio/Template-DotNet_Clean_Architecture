using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ChangePassword;

/// <summary>
/// The new password is held to <see cref="PasswordInputPolicy"/>; the password policy is the
/// store's. The current one gets presence only: whether it is right is the hasher's answer, and a
/// length rule here would refuse a password the store had already accepted under an older policy.
/// </summary>
public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    /// <summary>Presence for the current password, the password input policy for the new one.</summary>
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required.");
        RuleFor(x => x.NewPassword).WithinPasswordInputPolicy();
    }
}
