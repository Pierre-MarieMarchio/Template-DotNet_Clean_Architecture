using AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;

namespace AppTemplate.Application.Features.Auth.Ports.PasswordResetTokens;

/// <summary>
/// The two halves of password reset: minting a single-use token for an address, and redeeming it to
/// set a new password in one step.
/// <para>
/// Issued whatever the address's confirmation status: unlike <see cref="IEmailConfirmationTokens"/>,
/// a forgotten password locked behind a confirmation link the same holder cannot obtain would strand
/// them for good rather than merely delay them.
/// </para>
/// <para>
/// Composing and delivering the message is not here, for the reason given on
/// <see cref="IEmailConfirmationTokens"/>.
/// </para>
/// <para>
/// <b>Cancellation is observed on entry only</b>, for the reason given on <see cref="IUserAccounts"/>.
/// </para>
/// </summary>
public interface IPasswordResetTokens
{
    /// <returns><c>null</c> when the address names no account, so a caller cannot use this to probe for one.</returns>
    Task<PendingPasswordReset?> IssueAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems a token and sets <paramref name="newPassword"/> in one step. An unknown address is not
    /// short-circuited to failure ahead of the store, for the reason given on
    /// <see cref="IEmailConfirmationTokens.RedeemAsync"/>.
    /// </summary>
    Task<PasswordResetOutcome> RedeemAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);
}
