namespace AppTemplate.Application.Features.Auth.Ports.ExternalIdentity;

/// <param name="Identity">
/// Set only for <see cref="ExternalIdentityStatus.Verified"/>. A refusal carries nothing at all: the
/// claims of a token that failed verification are attacker-supplied text.
/// </param>
public sealed record ExternalIdentityOutcome(ExternalIdentityStatus Status, VerifiedExternalIdentity? Identity)
{
    public static ExternalIdentityOutcome Verified(VerifiedExternalIdentity identity) =>
        new(ExternalIdentityStatus.Verified, identity);

    public static ExternalIdentityOutcome Refused(ExternalIdentityStatus status) => new(status, null);
}
