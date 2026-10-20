namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

public interface IAccountsService
{
    Task<AccountRegistrationOutcome> RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task<CredentialCheckOutcome> VerifyCredentialAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task<bool> CanSignInAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<PasswordChangeOutcome> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<UserProfile?> FindProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<AccountDeletionOutcome> DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
