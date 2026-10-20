namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

public interface IPasskeyService
{
    Task<PasskeyRegistrationChallenge> BeginRegistrationAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<PasskeyRegistrationOutcome> CompleteRegistrationAsync(
        Guid userId,
        string credentialJson,
        string registrationState,
        string? name,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PasskeyCredential>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(
        Guid userId,
        byte[] credentialId,
        CancellationToken cancellationToken = default);

    Task<PasskeySignInChallenge> BeginSignInAsync(CancellationToken cancellationToken = default);

    Task<PasskeySignInOutcome> CompleteSignInAsync(
        string credentialJson,
        string authenticationState,
        CancellationToken cancellationToken = default);
}
