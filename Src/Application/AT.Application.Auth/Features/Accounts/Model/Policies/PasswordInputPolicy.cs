using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.Model.Policies;

public static class PasswordInputPolicy
{
    public const int MaximumLength = 256;

    internal static IRuleBuilderOptions<T, string> WithinPasswordInputPolicy<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        ruleBuilder
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(MaximumLength)
                .WithMessage($"Password must not exceed {MaximumLength} characters.");
}
