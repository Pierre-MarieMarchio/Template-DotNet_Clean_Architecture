namespace AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;

/// <summary>
/// The single-use tokens an account receives by mail: confirming the address, resetting a forgotten
/// password, and moving the account to a new address. Composing and delivering the message is not
/// here — whether a mail is worth sending and what a delivery failure means is the use case's
/// decision, not the store's that minted the token. Confirmation and reset are keyed by address,
/// because their caller is anonymous: no answer may tell an unknown address from a known one. The
/// email change is keyed by the caller's id, since the caller already authenticated; what it must
/// still not disclose is whether <em>the new</em> address is registered — see
/// <see cref="EmailChangeRequestOutcome"/>.
/// </summary>
public interface IEmailTokensService
{
    /// <returns>
    /// <c>null</c> when no account at that address is awaiting confirmation — unknown or already
    /// confirmed. The two are not distinguished, so a caller cannot use this to probe for accounts.
    /// </returns>
    Task<string?> IssueEmailConfirmationAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems a confirmation token. An already-confirmed address is not short-circuited to success,
    /// which would answer "does this address exist and is it confirmed?" for any caller with a junk
    /// token.
    /// </summary>
    /// <returns><c>true</c> when the address is now confirmed; <c>false</c> for an unknown address and an invalid or expired token alike.</returns>
    Task<bool> ConfirmEmailAsync(string email, string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mints a reset token whatever the address's confirmation status: a forgotten password locked
    /// behind a confirmation link the same holder cannot obtain would strand them for good.
    /// </summary>
    /// <returns><c>null</c> when the address names no account, so a caller cannot use this to probe for one.</returns>
    Task<string?> IssuePasswordResetAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems a reset token and sets <paramref name="newPassword"/> in one step, under the store's
    /// password policy. An unknown address is not told apart from an invalid token.
    /// </summary>
    Task<PasswordResetOutcome> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies <paramref name="currentPassword"/> and, on a match, mints a token for
    /// <paramref name="newEmail"/> — proof that a stolen session alone cannot move the account to an
    /// address the attacker controls.
    /// </summary>
    Task<EmailChangeRequestOutcome> IssueEmailChangeAsync(
        Guid userId,
        string currentPassword,
        string newEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems an email-change token and applies the change in one step. ASP.NET Identity rotates the
    /// security stamp as part of this call — see <c>IAccountsService.ChangePasswordAsync</c> for what
    /// that invalidates and what it does not.
    /// </summary>
    Task<EmailChangeOutcome> ChangeEmailAsync(
        Guid userId,
        string newEmail,
        string token,
        CancellationToken cancellationToken = default);
}
