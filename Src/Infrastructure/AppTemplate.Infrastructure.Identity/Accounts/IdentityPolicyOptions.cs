using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Accounts;

/// <summary>
/// Password, lockout and sign-in policy. Every member has a safe default, so a section that is
/// absent or only partly filled in tightens nothing and loosens nothing — the previous plain
/// auto-properties defaulted four booleans to <c>false</c> and the required length to <c>0</c>,
/// and then overwrote ASP.NET Identity's secure defaults with them.
/// </summary>
public sealed class IdentityPolicyOptions
{
    public const string SectionName = "Identity";

    /// <summary>A floor configuration cannot go below, enforced both by validation and by clamping.</summary>
    public const int AbsoluteMinimumPasswordLength = 8;

    public int PasswordRequiredLength { get; set; } = 12;

    public int PasswordRequiredUniqueChars { get; set; } = 4;

    public bool PasswordRequireDigit { get; set; } = true;

    public bool PasswordRequireLowercase { get; set; } = true;

    public bool PasswordRequireUppercase { get; set; } = true;

    public bool PasswordRequireNonAlphanumeric { get; set; } = true;

    /// <summary>Lockout is what bounds online password guessing. It is on by default.</summary>
    public bool LockoutEnabled { get; set; } = true;

    public int LockoutMaxFailedAccessAttempts { get; set; } = 5;

    public int LockoutDurationInMinutes { get; set; } = 15;

    public bool RequireConfirmedEmail { get; set; } = true;

    public bool RequireUniqueEmail { get; set; } = true;

    /// <summary>The value actually handed to ASP.NET Identity, never below the hard floor.</summary>
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

        if (options.PasswordRequiredLength > 256)
        {
            failures.Add($"'{IdentityPolicyOptions.SectionName}:PasswordRequiredLength' must not exceed 256.");
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
