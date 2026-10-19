namespace AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;

/// <summary>
/// An account's second factor: its holder enrolling and removing it, and the sign-in challenge that
/// asks for a code once a password is verified. Enrollment is reached by an authenticated caller
/// managing their own account and never issues a token; the challenge is reached by an anonymous
/// caller mid sign-in and never changes whether the factor is armed. Stripping the factor from
/// someone else's account is an administrator's act, and lives with
/// <c>IAccountAdministrationService</c>.
/// </summary>
public interface ITwoFactorService
{
    /// <summary>
    /// Provisions a secret if none is pending yet, and returns it either way — a second call before
    /// confirmation hands back the same one, so reloading the setup page does not invalidate a code
    /// the caller already scanned.
    /// </summary>
    Task<TwoFactorSetupOutcome> BeginSetupAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the current password and the first code against the pending secret and, on a match
    /// against both, turns two-factor sign-in on and mints a fresh set of recovery codes. Never
    /// armed by <see cref="BeginSetupAsync"/> alone — only a confirmed code proves the caller can
    /// produce one; the password is required for the reason <see cref="DisableAsync"/> gives.
    /// Refused on an account already armed, so the recovery codes its holder was shown stay the
    /// valid ones.
    /// </summary>
    Task<TwoFactorConfirmationOutcome> ConfirmSetupAsync(
        Guid userId,
        string currentPassword,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the current password and, on a match, turns two-factor sign-in back off and discards
    /// the secret. The password is required because a stolen session alone must not be able to strip
    /// the account's second factor.
    /// </summary>
    Task<TwoFactorDisableStatus> DisableAsync(
        Guid userId,
        string currentPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parks a verified password behind a short-lived, single-use challenge. Superseding: issuing a
    /// new one for the same account invalidates whichever came before it, so retrying <c>/login</c>
    /// cannot leave two live challenges an attacker could choose between.
    /// </summary>
    /// <exception cref="InvalidOperationException">No account has that id.</exception>
    Task<IssuedTwoFactorChallenge> IssueChallengeAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies <paramref name="code"/> against the account the challenge names — the authenticator
    /// app's current code first, a recovery code second — and consumes the challenge on a match. A
    /// wrong code leaves the challenge live, so the caller can retry until it expires or runs out of
    /// attempts.
    /// </summary>
    Task<TwoFactorRedemptionOutcome> RedeemChallengeAsync(
        string challengeToken,
        string code,
        CancellationToken cancellationToken = default);
}
