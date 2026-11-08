namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// What the client sends after running the provider's authorisation-code-with-PKCE flow itself.
/// </summary>
/// <param name="Provider">The provider's name as the operator wrote it in configuration.</param>
/// <param name="IdToken">The OpenID Connect <c>id_token</c> the provider issued to the client.</param>
public sealed record SignInWithExternalProviderRequest(string Provider, string IdToken);
