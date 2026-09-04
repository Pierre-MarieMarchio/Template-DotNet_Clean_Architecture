namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorChallenge;

/// <summary>
/// The second half of a two-step login: parking a verified password behind a short-lived, single-use
/// token, and later exchanging that token plus a code for the account it names.
/// <para>
/// Not <c>ITwoFactorEnrollmentService</c> — see there for why the two are split.
/// </para>
/// <para>
/// <b>Cancellation is observed on entry only, and cannot be propagated</b>, for the reason given on
/// <c>IUserAccountsService</c>.
/// </para>
/// </summary>
public interface ITwoFactorChallengeService
{
    /// <summary>
    /// Issues a challenge for an account whose password was just verified. Superseding: issuing a new
    /// one for the same account invalidates whichever one came before it, so retrying <c>/login</c>
    /// cannot leave two live challenges an attacker could choose between.
    /// </summary>
    Task<IssuedTwoFactorChallenge> IssueAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies <paramref name="code"/> against the account the challenge names — the authenticator
    /// app's current code first, a recovery code second — and consumes the challenge on a match. A
    /// wrong code leaves the challenge live, so the caller can retry until it expires.
    /// </summary>
    Task<TwoFactorRedemptionOutcome> RedeemAsync(
        string challengeToken,
        string code,
        CancellationToken cancellationToken = default);
}
