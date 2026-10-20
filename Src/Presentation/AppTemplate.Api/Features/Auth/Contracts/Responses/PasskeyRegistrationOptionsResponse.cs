namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>
/// What a client feeds to <c>navigator.credentials.create()</c>, and the state to post back with
/// the credential.
/// </summary>
/// <param name="CreationOptions">
/// The WebAuthn creation options, as a JSON string to pass as <c>publicKey</c>.
/// </param>
/// <param name="RegistrationState">Opaque to the client; posted back when the registration completes.</param>
public sealed record PasskeyRegistrationOptionsResponse(string CreationOptions, string RegistrationState);
