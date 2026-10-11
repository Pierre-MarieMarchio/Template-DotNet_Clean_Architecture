namespace AppTemplate.Application.Auth.Features.Auth.Ports.Passkeys;

/// <summary>
/// One half of a sign-in ceremony's first step: what the client feeds to
/// <c>navigator.credentials.get()</c> for an authenticator to offer a discoverable credential, and
/// the opaque state it posts back with the assertion that results.
/// </summary>
/// <param name="SignInOptions">The WebAuthn request options, as the adapter's implementation
/// serialises them for a browser.</param>
/// <param name="AuthenticationState">Opaque to the caller, for the same reason as
/// <see cref="PasskeyRegistrationChallenge.RegistrationState"/> on the registration side.</param>
public sealed record PasskeySignInChallenge(string SignInOptions, string AuthenticationState);
