namespace AppTemplate.Application.Auth.Features.Auth.Ports.ExternalIdentity;

/// <param name="Identity">
/// Set only for <see cref="ExternalIdentityStatus.Verified"/>. A refusal carries nothing at all: the
/// claims of a token that failed verification are attacker-supplied text.
/// </param>
public sealed record ExternalIdentityOutcome(ExternalIdentityStatus Status, VerifiedExternalIdentity? Identity)
{
    /// <summary>Signature, issuer, audience and validity window all held; the claims may be read.</summary>
    public static ExternalIdentityOutcome Verified(VerifiedExternalIdentity identity) =>
        new(ExternalIdentityStatus.Verified, identity);

    /// <summary>Verification failed. Carries the reason for a log line, and nothing the token said.</summary>
    public static ExternalIdentityOutcome Refused(ExternalIdentityStatus status) => new(status, null);
}
