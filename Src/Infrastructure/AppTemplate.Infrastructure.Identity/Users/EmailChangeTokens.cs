using AppTemplate.Application.Features.Auth.Ports.EmailChangeTokens;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Users;

/// <summary>
/// <see cref="IEmailChangeTokens"/> over ASP.NET Identity's named <c>ChangeEmail</c> token provider
/// — see <c>EmailChangeTokenProvider</c> for why it is a named provider of its own.
/// </summary>
internal sealed class EmailChangeTokens(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory) : IEmailChangeTokens
{
    public async Task<EmailChangeRequest> IssueAsync(
        Guid userId,
        string currentPassword,
        string newEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newEmail);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        // The caller already authenticated as this id, so there is no address to protect from
        // enumeration here, for the reason UserAccounts.ChangePasswordAsync gives: an absent account
        // only means it was deleted after the token that authenticated this request was issued.
        if (user is null)
        {
            return EmailChangeRequest.IncorrectCurrentPassword;
        }

        // Not userManager.CheckPasswordAsync: it rewrites the stored hash on a rehash-needed result,
        // which rotates the security stamp — and would invalidate the very session submitting this
        // request before the change it is asking for was even confirmed. VerifyHashedPassword alone
        // has no such side effect.
        if (user.PasswordHash is not { } hash ||
            userManager.PasswordHasher.VerifyHashedPassword(user, hash, currentPassword)
                is PasswordVerificationResult.Failed)
        {
            return EmailChangeRequest.IncorrectCurrentPassword;
        }

        var existing = await userManager.FindByEmailAsync(newEmail);

        // Suppressed rather than reported: revealing that the address is already registered — to
        // someone else, or to this same account — would turn "request a change" into a way to test
        // which addresses exist.
        if (existing is not null)
        {
            return EmailChangeRequest.Suppressed;
        }

        return EmailChangeRequest.Issued(
            user.UserName ?? string.Empty,
            await userManager.GenerateChangeEmailTokenAsync(user, newEmail));
    }

    public async Task<EmailChangeConfirmation> RedeemAsync(
        Guid userId,
        string newEmail,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newEmail);
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return EmailChangeConfirmation.NoSuchAccount;
        }

        // UserName is left untouched: Register lets a caller pick one independent of Email (see
        // RegisterCommand), so the two stay decoupled here too — moving the address must not
        // silently rename the account's sign-in identity. ChangeEmailAsync rotates the security
        // stamp as a side effect; the caller is responsible for revoking refresh tokens, which it
        // does not touch.
        var result = await userManager.ChangeEmailAsync(user, newEmail, token);

        if (result.Succeeded)
        {
            return EmailChangeConfirmation.Changed;
        }

        bool invalidToken = result.Errors.Any(error =>
            string.Equals(error.Code, "InvalidToken", StringComparison.Ordinal));

        return invalidToken
            ? EmailChangeConfirmation.InvalidToken
            : EmailChangeConfirmation.Rejected(string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}
