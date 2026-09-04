using FluentValidation;

namespace AppTemplate.Application.Features.Auth.Validators;

/// <summary>
/// The shape every new password must satisfy, shared by every validator that accepts one — sign-up,
/// change, reset — so the floor and ceiling live in one place rather than being copied per command
/// and drifting apart. The character-class policy itself is not here: that is owned by the validated
/// <c>Identity</c> configuration section and enforced by the store.
/// </summary>
internal static class PasswordRules
{
    /// <summary>Defence-in-depth floor mirroring the minimum the Identity configuration cannot go below.</summary>
    public const int AbsoluteMinimumPasswordLength = 8;

    /// <summary>Guards against a denial of service through an arbitrarily long PBKDF2 input.</summary>
    public const int MaximumPasswordLength = 256;

    public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        ruleBuilder
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(AbsoluteMinimumPasswordLength)
                .WithMessage($"Password must be at least {AbsoluteMinimumPasswordLength} characters long.")
            .MaximumLength(MaximumPasswordLength)
                .WithMessage($"Password must not exceed {MaximumPasswordLength} characters.");
}
