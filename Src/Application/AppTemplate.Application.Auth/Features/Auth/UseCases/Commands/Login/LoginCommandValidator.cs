using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Login;

/// <summary>
/// Presence only. Rejecting a malformed address here would tell an attacker which addresses are
/// even worth trying, and an existing account may predate the current password policy.
/// </summary>
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    /// <summary>Both fields are required, and neither is inspected any further.</summary>
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}
