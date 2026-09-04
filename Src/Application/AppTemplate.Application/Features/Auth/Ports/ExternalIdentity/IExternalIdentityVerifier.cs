namespace AppTemplate.Application.Features.Auth.Ports.ExternalIdentity;

/// <summary>
/// Answers one question about an OpenID Connect <c>id_token</c> the client obtained itself: is it
/// authentic, and what does it say?
/// <para>
/// This API is JSON and holds no cookie and no browser redirect, so the provider's flow runs on the
/// client and only the resulting <c>id_token</c> is posted here. What arrives is therefore an
/// assertion from a third party, and the only thing standing between it and an account is this
/// verification: the signature against the provider's JWKS, the issuer, the audience, and the
/// <c>exp</c>/<c>nbf</c> window.
/// </para>
/// <para>
/// It knows nothing about accounts. Whether a subject may sign in, which local account it belongs to
/// and whether one should be created are decisions
/// <c>SignInWithExternalProviderUseCase</c> makes from what this returns — an adapter that resolved
/// an account would be deciding the linking policy behind a seam no unit test can reach.
/// </para>
/// <para>
/// <b>The provider name is a value, not an enum.</b> Which providers exist is an operator's
/// configuration, so a name this implementation does not recognise is
/// <see cref="ExternalIdentityStatus.UnknownProvider"/> rather than a compile error.
/// </para>
/// </summary>
public interface IExternalIdentityVerifier
{
    /// <param name="provider">
    /// The provider the token claims to come from, as the client named it. It selects which issuer,
    /// audience and key set the token is checked against, so a token minted by one provider cannot be
    /// presented as another's.
    /// </param>
    Task<ExternalIdentityOutcome> VerifyAsync(
        string provider,
        string idToken,
        CancellationToken cancellationToken = default);
}
