using AppTemplate.Application.Auth.Features.Auth.Ports.EmailConfirmationTokens;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Services;

/// <summary>
/// <see cref="IEmailConfirmationTokensService"/> over ASP.NET Identity's default token provider.
/// </summary>
internal sealed class EmailConfirmationTokensService(UserManager<AppUser> userManager) : IEmailConfirmationTokensService
{
    public async Task<PendingConfirmation?> IssueAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);

        if (user is not { EmailConfirmed: false })
        {
            return null;
        }

        return new PendingConfirmation(
            user.UserName ?? string.Empty,
            await userManager.GenerateEmailConfirmationTokenAsync(user));
    }

    public async Task<EmailConfirmationStatus> RedeemAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            return EmailConfirmationStatus.NoSuchAccount;
        }

        var result = await userManager.ConfirmEmailAsync(user, token);

        if (!result.Succeeded)
        {
            return EmailConfirmationStatus.InvalidToken;
        }

        // ConfirmEmailAsync does not rotate the security stamp, unlike ChangePasswordAsync and
        // ChangeEmailAsync. A DataProtectorTokenProvider token embeds the stamp and is refused once
        // it no longer matches, so without this the token just redeemed stays replayable.
        await userManager.UpdateSecurityStampAsync(user);

        return EmailConfirmationStatus.Confirmed;
    }
}
