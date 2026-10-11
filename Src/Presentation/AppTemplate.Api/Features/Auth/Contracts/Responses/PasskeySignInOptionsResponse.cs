namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>
/// The first step of signing in with a passkey: what the client feeds to
/// <c>navigator.credentials.get()</c> for an authenticator to offer a discoverable credential, and
/// the state to post back with the assertion that results.
/// </summary>
/// <param name="SignInOptions">The WebAuthn request options, as a JSON string the client parses
/// and passes to <c>navigator.credentials.get()</c> as its <c>publicKey</c>.</param>
/// <param name="AuthenticationState">Opaque to the client; it names the ceremony and is verified
/// against the assertion when the sign-in completes.</param>
public sealed record PasskeySignInOptionsResponse(string SignInOptions, string AuthenticationState);
