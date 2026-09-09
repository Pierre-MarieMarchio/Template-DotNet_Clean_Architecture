namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// What the client sends after running the provider's authorisation-code-with-PKCE flow itself.
/// </summary>
/// <remarks>
/// The provider is a field of the body and not a segment of the route, so an unknown provider is a
/// 400 like every other rejected field rather than a 404 — see <c>docs/ARCHITECTURE.md</c>.
/// </remarks>
/// <param name="Provider">
/// The provider's name as the operator wrote it in configuration. No server-side constant lists the
/// accepted values, and a name nobody configured is refused exactly like a forged token.
/// </param>
/// <param name="IdToken">The OpenID Connect <c>id_token</c> the provider issued to the client.</param>
public sealed record SignInWithExternalProviderRequest(string Provider, string IdToken);
