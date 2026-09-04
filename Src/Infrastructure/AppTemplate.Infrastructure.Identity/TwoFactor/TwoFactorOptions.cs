using AppTemplate.Infrastructure.Identity.Accounts;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.TwoFactor;

/// <summary>
/// How long a login challenge stays redeemable, how many recovery codes enrollment mints, and the
/// issuer label an authenticator app shows next to the account. Every member has a safe default, for
/// the same reason <see cref="IdentityPolicyOptions"/> gives.
/// </summary>
public sealed class TwoFactorOptions
{
    public const string SectionName = "TwoFactor";

    /// <summary>
    /// Deliberately short: the challenge is what stands between a verified password and a token
    /// pair, and unlike a refresh token it is not something a legitimate caller is expected to hold
    /// on to.
    /// </summary>
    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How many single-use recovery codes enrollment mints when two-factor sign-in is confirmed.</summary>
    public int RecoveryCodeCount { get; set; } = 10;

    /// <summary>
    /// The issuer name every enrolled authenticator app displays next to the account. Rename this
    /// alongside every other "AppTemplate" occurrence a fork replaces — see the dotnet-new template
    /// configuration — since it is what a user sees on their phone, not just internal wiring.
    /// </summary>
    public string Issuer { get; set; } = "AppTemplate";
}

internal sealed class TwoFactorOptionsValidator : IValidateOptions<TwoFactorOptions>
{
    private static readonly TimeSpan _minimumChallengeLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _maximumChallengeLifetime = TimeSpan.FromMinutes(30);

    public ValidateOptionsResult Validate(string? name, TwoFactorOptions options)
    {
        var failures = new List<string>();

        if (options.ChallengeLifetime < _minimumChallengeLifetime || options.ChallengeLifetime > _maximumChallengeLifetime)
        {
            failures.Add(
                $"'{TwoFactorOptions.SectionName}:ChallengeLifetime' must be between " +
                $"{_minimumChallengeLifetime} and {_maximumChallengeLifetime}.");
        }

        if (options.RecoveryCodeCount is < 1 or > 20)
        {
            failures.Add($"'{TwoFactorOptions.SectionName}:RecoveryCodeCount' must be between 1 and 20.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add($"'{TwoFactorOptions.SectionName}:Issuer' is required.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
