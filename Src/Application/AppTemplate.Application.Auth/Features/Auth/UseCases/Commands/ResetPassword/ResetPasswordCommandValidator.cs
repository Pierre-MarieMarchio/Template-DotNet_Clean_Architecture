using AppTemplate.Application.Auth.Features.Auth.Policies;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ResetPassword;

/// <summary>
/// Presence for the address and the token, the shared bounds for the new password. No address
/// format rule, for the reason <c>RequestPasswordResetCommandValidator</c> gives.
/// </summary>
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    /// <summary>Address and token required; the password held to <c>PasswordPolicy</c>'s bounds.</summary>
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Reset token is required.");
        RuleFor(x => x.NewPassword).Password();
    }
}
