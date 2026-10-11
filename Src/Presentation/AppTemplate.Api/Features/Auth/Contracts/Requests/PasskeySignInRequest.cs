namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// Completes a passkey sign-in: the assertion a client obtained from the sign-in options, and the
/// state that came with them.
/// </summary>
/// <param name="Credential">The JSON-serialised result of <c>navigator.credentials.get()</c>.</param>
/// <param name="AuthenticationState">The opaque state string the sign-in options returned.</param>
public sealed record PasskeySignInRequest(string Credential, string AuthenticationState);
