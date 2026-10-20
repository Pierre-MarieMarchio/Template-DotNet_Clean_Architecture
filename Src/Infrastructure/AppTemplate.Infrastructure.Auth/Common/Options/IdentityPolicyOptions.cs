using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Common.Options;

public sealed class IdentityPolicyOptions
{
    public const string SectionName = "Identity";

    public const int AbsoluteMinimumPasswordLength = 8;

    public int PasswordRequiredLength { get; set; } = 12;

    public int PasswordRequiredUniqueChars { get; set; } = 4;

    public bool PasswordRequireDigit { get; set; } = true;

    public bool PasswordRequireLowercase { get; set; } = true;

    public bool PasswordRequireUppercase { get; set; } = true;

    public bool PasswordRequireNonAlphanumeric { get; set; } = true;

    public bool LockoutEnabled { get; set; } = true;

    public int LockoutMaxFailedAccessAttempts { get; set; } = 5;

    public int LockoutDurationInMinutes { get; set; } = 15;

    public bool RequireConfirmedEmail { get; set; } = true;

    public bool RequireUniqueEmail { get; set; } = true;

    internal int EffectivePasswordRequiredLength =>
        Math.Max(PasswordRequiredLength, AbsoluteMinimumPasswordLength);
}

internal sealed class IdentityPolicyOptionsValidator : IValidateOptions<IdentityPolicyOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityPolicyOptions options)
    {
        var failures = new List<string>();

        if (options.PasswordRequiredLength < IdentityPolicyOptions.AbsoluteMinimumPasswordLength)
        {
            failures.Add(
                $"'{IdentityPolicyOptions.SectionName}:PasswordRequiredLength' must be at least " +
                $"{IdentityPolicyOptions.AbsoluteMinimumPasswordLength}.");
        }

        if (options.PasswordRequiredLength > PasswordInputPolicy.MaximumLength)
        {
            failures.Add(
                $"'{IdentityPolicyOptions.SectionName}:PasswordRequiredLength' must not exceed " +
                $"{PasswordInputPolicy.MaximumLength}.");
        }

        if (options.PasswordRequiredUniqueChars < 1)
        {
            failures.Add($"'{IdentityPolicyOptions.SectionName}:PasswordRequiredUniqueChars' must be at least 1.");
        }

        if (options.LockoutMaxFailedAccessAttempts is < 1 or > 20)
        {
            failures.Add(
                $"'{IdentityPolicyOptions.SectionName}:LockoutMaxFailedAccessAttempts' must be between 1 and 20.");
        }

        if (options.LockoutDurationInMinutes < 1)
        {
            failures.Add($"'{IdentityPolicyOptions.SectionName}:LockoutDurationInMinutes' must be at least 1.");
        }

        if (!options.RequireUniqueEmail)
        {
            failures.Add(
                $"'{IdentityPolicyOptions.SectionName}:RequireUniqueEmail' cannot be disabled: the user table " +
                "carries a unique index on the normalised email.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
