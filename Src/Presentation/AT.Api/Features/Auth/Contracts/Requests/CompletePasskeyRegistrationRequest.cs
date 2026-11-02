namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// The created credential, the state its options came with, an optional label and the current
/// password.
/// </summary>
/// <param name="Credential">The JSON-serialised result of <c>navigator.credentials.create()</c>.</param>
/// <param name="RegistrationState">The opaque state string the registration options returned.</param>
/// <param name="Name">An optional label for the credential, shown in the passkey list.</param>
/// <param name="CurrentPassword">The account's password.</param>
public sealed record CompletePasskeyRegistrationRequest(
    string Credential,
    string RegistrationState,
    string? Name,
    string? CurrentPassword);
