namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.CompletePasskeyRegistration;

/// <summary>Carries no identity: the account is the authenticated caller's own.</summary>
/// <param name="Credential">The JSON-serialised result of <c>navigator.credentials.create()</c>,
/// as the client's browser produced it.</param>
/// <param name="RegistrationState">The opaque string the registration options step returned, which
/// the ceremony verifies the credential against.</param>
/// <param name="Name">The label the holder gives the credential — "YubiKey 5", "iPhone" — or
/// <c>null</c> to leave it unnamed.</param>
/// <param name="CurrentPassword">The account's password, when it has one — which evidence is
/// required is decided from the profile, not from what this carries.</param>
public sealed record CompletePasskeyRegistrationCommand(
    string Credential,
    string RegistrationState,
    string? Name,
    string? CurrentPassword);
