namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

/// <summary>
/// One half of a registration ceremony's first step: what the client feeds to
/// <c>navigator.credentials.create()</c>, and the opaque state it posts back with the credential
/// that results. The state is the adapter's to read and the client's to carry: no server-side
/// session joins the two calls.
/// </summary>
/// <param name="CreationOptions">The WebAuthn creation options, as the adapter's implementation
/// serialises them for a browser.</param>
/// <param name="RegistrationState">Opaque to the caller. Names the ceremony's challenge and
/// account, and is verified against the credential when the ceremony completes.</param>
public sealed record PasskeyRegistrationChallenge(string CreationOptions, string RegistrationState);
