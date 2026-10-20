namespace AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;

public interface IAccountAdministrationService
{
    Task<AccountAdministrationOutcome> LockAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<AccountAdministrationOutcome> UnlockAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<AccountAdministrationOutcome> GrantRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);

    Task<AccountAdministrationOutcome> RevokeRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);

    Task<AccountAdministrationOutcome> DisableTwoFactorAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<AccountAdministrationOutcome> DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
