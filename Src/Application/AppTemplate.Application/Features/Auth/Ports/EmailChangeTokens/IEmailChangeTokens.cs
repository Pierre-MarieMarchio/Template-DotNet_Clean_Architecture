namespace AppTemplate.Application.Features.Auth.Ports.EmailChangeTokens;

/// <summary>
/// The two halves of an authenticated email-address change: minting a single-use token for the new
/// address after proving the current password, and redeeming it to apply the change.
/// <para>
/// Unlike <c>IEmailConfirmationTokens</c> and <c>IPasswordResetTokens</c>, both halves are keyed by
/// the caller's id rather than by an email address: the caller already authenticated, so there is no
/// anonymous address to protect from enumeration on the account-lookup side. What still must not be
/// disclosed is whether <em>the new</em> address is already registered — see
/// <see cref="EmailChangeRequestOutcome"/>.
/// </para>
/// <para>
/// Composing and delivering the message is not here, for the reason given on
/// <c>IEmailConfirmationTokens</c>.
/// </para>
/// <para>
/// <b>Cancellation is observed on entry only, and cannot be propagated</b>, for the reason given on
/// <c>IUserAccounts</c>.
/// </para>
/// </summary>
public interface IEmailChangeTokens
{
    /// <summary>
    /// Verifies <paramref name="currentPassword"/> and, on a match, mints a token for
    /// <paramref name="newEmail"/> — proof that a stolen session alone cannot move the account to an
    /// address the attacker controls.
    /// </summary>
    Task<EmailChangeRequestOutcome> IssueAsync(
        Guid userId,
        string currentPassword,
        string newEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems a token and applies the change in one step. ASP.NET Identity rotates the security
    /// stamp as part of this call — see <c>IUserAccounts.ChangePasswordAsync</c> for what that
    /// invalidates and what it does not.
    /// </summary>
    Task<EmailChangeConfirmationOutcome> RedeemAsync(
        Guid userId,
        string newEmail,
        string token,
        CancellationToken cancellationToken = default);
}
