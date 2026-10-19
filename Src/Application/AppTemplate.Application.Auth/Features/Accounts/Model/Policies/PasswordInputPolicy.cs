using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.Model.Policies;

/// <summary>
/// The input bounds every new password is checked against before it reaches the store: present, and
/// no longer than <see cref="MaximumLength"/>. They are not the password policy. Length and
/// character classes have one authority, the validated <c>Identity</c> configuration section, which
/// the store applies to every new password; a second copy here would drift from it.
/// </summary>
public static class PasswordInputPolicy
{
    /// <summary>
    /// Guards against a denial of service through an arbitrarily long PBKDF2 input. The configured
    /// policy may not require more.
    /// </summary>
    public const int MaximumLength = 256;

    internal static IRuleBuilderOptions<T, string> WithinPasswordInputPolicy<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        ruleBuilder
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(MaximumLength)
                .WithMessage($"Password must not exceed {MaximumLength} characters.");
}
