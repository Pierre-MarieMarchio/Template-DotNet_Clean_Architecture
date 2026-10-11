namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// Completes a passkey registration: the credential a client created from the registration
/// options, the state that came with them, and the proof the session is the account's own.
/// </summary>
/// <param name="Credential">The JSON-serialised result of <c>navigator.credentials.create()</c>.</param>
/// <param name="RegistrationState">The opaque state string the registration options returned.</param>
/// <param name="Name">An optional label for the credential, shown in the passkey list.</param>
/// <param name="CurrentPassword">The account's password. Required when the account has one; an
/// account with none — a provider-linked one — is held instead to a recently issued access token.</param>
public sealed record CompletePasskeyRegistrationRequest(
    string Credential,
    string RegistrationState,
    string? Name,
    string? CurrentPassword);
