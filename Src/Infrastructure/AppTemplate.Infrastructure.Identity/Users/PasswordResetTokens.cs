using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Users;

/// <summary>
/// <see cref="IPasswordResetTokens"/> over ASP.NET Identity's <c>PasswordReset</c> token provider —
/// see <c>PasswordResetTokenProvider</c> for why it is a named provider of its own rather than the
/// one <see cref="EmailConfirmationTokens"/> shares with every other default provider.
/// </summary>
internal sealed class PasswordResetTokens(UserManager<AppUser> userManager) : IPasswordResetTokens
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

    public async Task<PasswordReset> RedeemAsync(
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
            return PasswordReset.NoSuchAccount;
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);

        if (result.Succeeded)
        {
            return PasswordReset.Succeeded(user.Id);
        }

        bool invalidToken = result.Errors.Any(error =>
            string.Equals(error.Code, "InvalidToken", StringComparison.Ordinal));

        return invalidToken
            ? PasswordReset.InvalidToken
            : PasswordReset.Rejected(string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}
