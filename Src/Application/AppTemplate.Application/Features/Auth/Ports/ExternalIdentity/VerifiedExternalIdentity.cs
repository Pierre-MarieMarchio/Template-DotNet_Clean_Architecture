namespace AppTemplate.Application.Features.Auth.Ports.ExternalIdentity;

/// <summary>What an authentic <c>id_token</c> said, and nothing the verifier inferred on top of it.</summary>
/// <param name="Subject">
/// The provider's own immutable identifier for the account — <c>sub</c>. Together with
/// <paramref name="Provider"/> this is the only key a local account is ever resolved by: it is the
/// one claim a provider guarantees is stable, whereas an address can be changed, reassigned, or
/// simply not sent.
/// </param>
/// <param name="Email">
/// <c>null</c> whenever the token carried no address. That is the normal case, not an error: Apple
/// returns the address on the first authorisation only, so every later token for the same user has
/// none. A flow that needed one here would work in development and fail on a user's second sign-in.
/// </param>
/// <param name="EmailVerified">
/// Whether the provider states it checked the address — <c>email_verified</c>. <c>false</c> when the
/// claim is absent, so an omission is never read as an assurance.
/// </param>
public sealed record VerifiedExternalIdentity(
    string Provider,
    string Subject,
    string? Email,
    bool EmailVerified);
