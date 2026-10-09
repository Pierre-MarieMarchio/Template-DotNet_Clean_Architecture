using AppTemplate.Application.Auth.Features.Auth.Policies;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Register;

/// <summary>
/// Shape only. The password policy is the validated <c>Identity</c> configuration section, applied by
/// the store; this checks only <see cref="PasswordInputPolicy"/>.
/// </summary>
public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    /// <summary>Guards against a denial of service through an arbitrarily long PBKDF2 input.</summary>
    public const int MaximumPasswordLength = PasswordInputPolicy.MaximumLength;

    /// <summary>A bound of this layer's own choosing, wide enough that no legitimate name meets it.</summary>
    public const int MaximumUserNameLength = 64;

    /// <summary>
    /// A bounded user name, an address of plausible shape and length, and the password input policy.
    /// </summary>
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

        RuleFor(x => x.Password).WithinPasswordInputPolicy();
    }
}
