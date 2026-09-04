using AppTemplate.Infrastructure.Identity.Common.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Options;

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
    /// How many wrong codes one challenge tolerates before it is destroyed and the caller has to
    /// present the password again.
    /// </summary>
    /// <remarks>
    /// <b>This is the only thing that bounds guessing a second factor.</b> Account lockout does not:
    /// it counts failed <em>password</em> checks — <c>CheckPasswordSignInAsync</c> with
    /// <c>lockoutOnFailure</c> — and presenting a code is not one, so without a ceiling here a caller
    /// who already holds the password could offer codes for the whole challenge lifetime and be
    /// stopped only by the rate limiter, which is per process and therefore per replica.
    /// <para>
    /// Five, because a person mistypes a six-digit code once or twice and a clock a step out of
    /// alignment costs at most one more. It buys back the password check rather than locking the
    /// account: destroying the challenge costs an attacker the whole password exchange per five
    /// guesses, and costs the legitimate owner one re-login.
    /// </para>
    /// </remarks>
    public int MaxChallengeAttempts { get; set; } = 5;

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

        // A floor of one rather than zero: zero would destroy the challenge on the first wrong code,
        // which is indistinguishable from having no second factor a user can actually pass.
        if (options.MaxChallengeAttempts is < 1 or > 20)
        {
            failures.Add(
                $"'{TwoFactorOptions.SectionName}:MaxChallengeAttempts' must be between 1 and 20. It is " +
                "the only bound on guessing a code, so a large value is a decision rather than a default.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add($"'{TwoFactorOptions.SectionName}:Issuer' is required.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
