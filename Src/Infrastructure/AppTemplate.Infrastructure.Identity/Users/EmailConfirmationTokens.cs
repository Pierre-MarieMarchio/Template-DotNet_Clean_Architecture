using AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Users;

/// <summary>
/// <see cref="IEmailConfirmationTokens"/> over ASP.NET Identity's default token provider.
/// </summary>
internal sealed class EmailConfirmationTokens(UserManager<AppUser> userManager) : IEmailConfirmationTokens
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

    public async Task<EmailConfirmationOutcome> RedeemAsync(
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
            return EmailConfirmationOutcome.NoSuchAccount;
        }

        var result = await userManager.ConfirmEmailAsync(user, token);

        return result.Succeeded ? EmailConfirmationOutcome.Confirmed : EmailConfirmationOutcome.InvalidToken;
    }
}
