namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorChallenge;

/// <summary>
/// Why redeeming a challenge ended the way it did. Both non-<see cref="Verified"/> members must
/// reach a caller as the same error — see <c>AuthErrors.InvalidTwoFactorChallenge</c>.
/// </summary>
public enum TwoFactorRedemptionOutcome
{
    Verified,

    /// <summary>The token is unknown, malformed, expired, or already redeemed.</summary>
    InvalidChallenge,

    /// <summary>The challenge is live, but the code did not match it — neither the authenticator app nor a recovery code.</summary>
    InvalidCode,
}
