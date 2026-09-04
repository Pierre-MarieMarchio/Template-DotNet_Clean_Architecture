using AppTemplate.Application.Features.Auth.Ports.PasswordResetTokens;
using AppTemplate.Infrastructure.Identity.EmailConfirmation;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.PasswordReset;

/// <summary>
/// <see cref="IPasswordResetTokensService"/> over ASP.NET Identity's <c>PasswordReset</c> token provider —
/// see <c>PasswordResetTokenProvider</c> for why it is a named provider of its own rather than the
/// one <see cref="EmailConfirmationTokensService"/> shares with every other default provider.
/// </summary>
internal sealed class PasswordResetTokensService(UserManager<AppUser> userManager) : IPasswordResetTokensService
{
    public async Task<PendingPasswordReset?> IssueAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            return null;
        }

        return new PendingPasswordReset(
            user.UserName ?? string.Empty,
            await userManager.GeneratePasswordResetTokenAsync(user));
    }

    public async Task<PasswordResetOutcome> RedeemAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(newPassword);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            return PasswordResetOutcome.NoSuchAccount;
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);

        if (result.Succeeded)
        {
            return PasswordResetOutcome.Succeeded(user.Id);
        }

        bool invalidToken = result.Errors.Any(error =>
            string.Equals(error.Code, "InvalidToken", StringComparison.Ordinal));

        return invalidToken
            ? PasswordResetOutcome.InvalidToken
            : PasswordResetOutcome.Rejected(string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}
