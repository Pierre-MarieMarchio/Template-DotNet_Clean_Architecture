namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>
/// What a client feeds to <c>navigator.credentials.get()</c>, and the state to post back with the
/// assertion.
/// </summary>
/// <param name="SignInOptions">
/// The WebAuthn request options, as a JSON string to pass as <c>publicKey</c>.
/// </param>
/// <param name="AuthenticationState">Opaque to the client; posted back when the sign-in completes.</param>
public sealed record PasskeySignInOptionsResponse(string SignInOptions, string AuthenticationState);
