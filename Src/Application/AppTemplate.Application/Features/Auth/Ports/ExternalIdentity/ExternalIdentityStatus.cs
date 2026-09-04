namespace AppTemplate.Application.Features.Auth.Ports.ExternalIdentity;

/// <summary>
/// How verifying an <c>id_token</c> ended.
/// <para>
/// The reasons are separated so the sign-in policy is visible where the decision is made, and every
/// one of them that is not <see cref="Verified"/> must reach the caller as the same error — the same
/// rule, and for the same reason, as
/// <see cref="AppTemplate.Application.Features.Auth.Ports.UserAccounts.CredentialCheckStatus"/>.
/// </para>
/// </summary>
public enum ExternalIdentityStatus
{
    Verified,

    /// <summary>
    /// No provider is configured under that name. Not distinguished from
    /// <see cref="InvalidToken"/> by the caller: telling them apart would let anyone enumerate which
    /// providers an installation accepts before trying to forge anything for one.
    /// </summary>
    UnknownProvider,

    /// <summary>
    /// The signature, issuer, audience or validity window did not hold, or the token was not a
    /// readable JWT at all. One value for all of them, because a caller presenting a token it did not
    /// obtain honestly must not be told which check it failed.
    /// </summary>
    InvalidToken,
}
