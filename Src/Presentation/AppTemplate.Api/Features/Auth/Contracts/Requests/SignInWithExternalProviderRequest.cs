namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// What the client sends after running the provider's authorisation-code-with-PKCE flow itself.
/// </summary>
/// <remarks>
/// The provider is a field of the body rather than a segment of the route, and that is a security
/// decision rather than a stylistic one: routing is the one layer of this API that answers before any
/// code does, so a provider named in the path makes "is this provider configured?" a question the
/// router could be made to answer — a constraint, a catch-all that does not match, or simply a second
/// route added later, and an unknown provider becomes a 404 where a configured one is a 401. With one
/// route and one handler there is nothing for routing to disclose. It also keeps
/// <c>SignInWithExternalProviderCommandValidator</c>'s presence rule reachable: a route segment can
/// never bind empty, so the rule would be dead code and a missing provider would surface as a 404
/// instead of the 400 every other required field of this API produces.
/// </remarks>
/// <param name="Provider">
/// The provider's name as the operator wrote it in configuration. No server-side constant lists the
/// accepted values, and a name nobody configured is refused exactly like a forged token.
/// </param>
/// <param name="IdToken">The OpenID Connect <c>id_token</c> the provider issued to the client.</param>
public sealed record SignInWithExternalProviderRequest(string Provider, string IdToken);
