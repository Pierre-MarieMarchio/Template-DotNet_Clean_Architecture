namespace AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;

public interface IAccountEmailFactory
{
    Task<AccountEmail> CreateConfirmationAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default);

    Task<AccountEmail> CreatePasswordResetAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default);

    Task<AccountEmail> CreateEmailChangeAsync(
        string newEmail,
        string token,
        CancellationToken cancellationToken = default);

    Task<AccountEmail> CreateAlreadyRegisteredAsync(string email, CancellationToken cancellationToken = default);
}
