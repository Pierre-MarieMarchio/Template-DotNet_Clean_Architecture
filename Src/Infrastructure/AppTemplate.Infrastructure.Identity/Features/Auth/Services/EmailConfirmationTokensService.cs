using AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Services;

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

        // Unlike ChangePasswordAsync, ResetPasswordAsync and ChangeEmailAsync, ConfirmEmailAsync does
        // not rotate the security stamp on its own. Every token minted by a DataProtectorTokenProvider
        // embeds the stamp at generation time and is rejected once it no longer matches the user's
        // current one — without this call, the token just redeemed stays both valid and replayable
        // until it expires, even though ConfirmEmailCommand documents it as single-use.
        await userManager.UpdateSecurityStampAsync(user);

        return EmailConfirmationStatus.Confirmed;
    }
}
