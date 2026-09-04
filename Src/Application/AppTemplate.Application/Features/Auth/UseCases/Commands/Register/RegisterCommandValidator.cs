using AppTemplate.Application.Features.Auth.Policies;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.Register;

/// <summary>
/// Shape only. The password policy itself is owned by the validated <c>Identity</c> configuration
/// section; do not add character-class rules here, they would drift from what the store enforces.
/// </summary>
public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    /// <summary>Defence-in-depth floor mirroring the minimum the Identity configuration cannot go below.</summary>
    public const int AbsoluteMinimumPasswordLength = PasswordRules.AbsoluteMinimumPasswordLength;

    /// <summary>Guards against a denial of service through an arbitrarily long PBKDF2 input.</summary>
    public const int MaximumPasswordLength = PasswordRules.MaximumPasswordLength;

    public const int MaximumUserNameLength = 64;

    public RegisterCommandValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("Username is required.")
            .MaximumLength(MaximumUserNameLength)
                .WithMessage($"Username must not exceed {MaximumUserNameLength} characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.")
            .EmailAddress().WithMessage("Invalid email format.");

        RuleFor(x => x.Password).Password();
    }
}
