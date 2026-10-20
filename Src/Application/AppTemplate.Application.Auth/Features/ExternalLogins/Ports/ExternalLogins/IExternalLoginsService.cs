using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

namespace AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalLogins;

public interface IExternalLoginsService
{
    Task<AccountIdentity?> FindByExternalLoginAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default);

    Task<LocalAccountMatch?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> LinkAsync(
        Guid userId,
        string provider,
        string subject,
        CancellationToken cancellationToken = default);

    Task<AccountIdentity?> ProvisionAsync(
        string email,
        string provider,
        string subject,
        CancellationToken cancellationToken = default);
}
