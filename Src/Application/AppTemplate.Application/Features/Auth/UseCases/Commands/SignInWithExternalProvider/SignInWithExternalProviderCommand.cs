namespace AppTemplate.Application.Features.Auth.UseCases.Commands.SignInWithExternalProvider;

/// <param name="Provider">
/// Which identity provider minted <paramref name="IdToken"/>, as the operator named it in
/// configuration. A value rather than an enum: adding a provider is an operator's change, not a
/// release.
/// </param>
/// <param name="IdToken">
/// The OpenID Connect <c>id_token</c> the client obtained from that provider by running the
/// authorisation-code-with-PKCE flow itself. Never an access token, and never anything this API
/// issued.
/// </param>
public sealed record SignInWithExternalProviderCommand(string Provider, string IdToken);
