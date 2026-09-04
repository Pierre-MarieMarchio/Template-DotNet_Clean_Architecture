using AppTemplate.Application.Features.Auth.Ports.UserAccounts;

namespace AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;

/// <summary>
/// The two halves of email confirmation: minting a single-use token for an address that is waiting
/// for one, and redeeming it.
/// <para>
/// Composing and delivering the message is not here. A caller that holds a token decides whether a
/// mail is worth sending and what a delivery failure means, and that decision belongs to the use
/// case rather than to the store that minted the token.
/// </para>
/// <para>
/// <b>Cancellation is observed on entry only</b>, for the reason given on <see cref="IUserAccountsService"/>.
/// </para>
/// </summary>
public interface IEmailConfirmationTokensService
{
    /// <returns>
    /// <c>null</c> when no account at that address is awaiting confirmation — unknown or already
    /// confirmed. The two are not distinguished, so a caller cannot use this to probe for accounts.
    /// </returns>
    Task<PendingConfirmation?> IssueAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems a token. An already-confirmed address is not short-circuited to success, which would
    /// answer "does this address exist and is it confirmed?" for any caller with a junk token.
    /// </summary>
    Task<EmailConfirmationStatus> RedeemAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default);
}
