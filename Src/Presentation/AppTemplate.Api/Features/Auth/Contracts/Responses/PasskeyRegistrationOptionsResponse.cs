namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>
/// The first step of registering a passkey: what the client feeds to
/// <c>navigator.credentials.create()</c>, and the state to post back with the credential that
/// results.
/// </summary>
/// <param name="CreationOptions">The WebAuthn creation options, as a JSON string the client parses
/// and passes to <c>navigator.credentials.create()</c> as its <c>publicKey</c>.</param>
/// <param name="RegistrationState">Opaque to the client; it names the ceremony and is verified
/// against the credential when registration completes.</param>
public sealed record PasskeyRegistrationOptionsResponse(string CreationOptions, string RegistrationState);
