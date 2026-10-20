using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;

/// <summary>
/// Renders the mails an account's address receives. It does not deliver them: an implementation owns the templates and the address of the page each link points
/// at, both deployment concerns, while whether to send and what a failed delivery means are the use
/// case's, through <see cref="IEmailSender"/>.
/// </summary>
public interface IAccountEmailFactory
{
    /// <summary>The link that confirms <paramref name="email"/>.</summary>
    Task<AccountEmail> CreateConfirmationAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>The link that sets a new password for the account at <paramref name="email"/>.</summary>
    Task<AccountEmail> CreatePasswordResetAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>The link, sent to <paramref name="newEmail"/>, that moves the account there.</summary>
    Task<AccountEmail> CreateEmailChangeAsync(
        string newEmail,
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>The notice, sent to <paramref name="email"/>, that an account already holds it.</summary>
    Task<AccountEmail> CreateAlreadyRegisteredAsync(string email, CancellationToken cancellationToken = default);
}
