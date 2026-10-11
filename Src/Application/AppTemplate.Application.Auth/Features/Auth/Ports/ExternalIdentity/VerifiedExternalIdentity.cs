namespace AppTemplate.Application.Auth.Features.Auth.Ports.ExternalIdentity;

/// <summary>What an authentic <c>id_token</c> said, and nothing the verifier inferred on top of it.</summary>
/// <param name="Subject">The provider's immutable identifier for the account — <c>sub</c>; with
/// <paramref name="Provider"/>, the only key a local account is ever resolved by, it being the one
/// claim a provider guarantees is stable.</param>
/// <param name="Email"><c>null</c> when the token carried no address — the normal case, not an
/// error: Apple returns the address on the first authorisation only.</param>
/// <param name="EmailVerified">Whether the provider states it checked the address —
/// <c>email_verified</c>; <c>false</c> when the claim is absent, so an omission is never read as
/// an assurance.</param>
public sealed record VerifiedExternalIdentity(
    string Provider,
    string Subject,
    string? Email,
    bool EmailVerified);
