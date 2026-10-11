namespace AppTemplate.Application.Auth.Features.Auth.Ports.ExternalIdentity;

/// <summary>Answers one question about an OpenID Connect <c>id_token</c> the client obtained
/// itself: is it authentic, and what does it say? What arrives is a third party's assertion, so
/// this checks the signature against the provider's JWKS, the issuer, the audience, and the
/// <c>exp</c>/<c>nbf</c> window. It knows nothing about accounts — whether a subject may sign in
/// and whether one is created are <c>SignInWithExternalProviderUseCase</c>'s decisions.</summary>
/// <remarks>The provider name is a value, not an enum: a name this implementation does not
/// recognise is a refusal like any other, not a compile error.</remarks>
public interface IExternalIdentityVerifier
{
    /// <param name="provider">The provider the token claims to come from, as the client named it;
    /// it selects the issuer, audience and key set checked against, so one provider's token cannot
    /// be presented as another's.</param>
    /// <returns>The verified identity, or <c>null</c> for an unknown provider and a token that fails any check alike: the caller answers every refusal the same way.</returns>
    Task<VerifiedExternalIdentity?> VerifyAsync(
        string provider,
        string idToken,
        CancellationToken cancellationToken = default);
}
