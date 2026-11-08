using AppTemplate.Application.Auth.Features.Accounts.Ports.ReAuthentication;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;

internal sealed class ReAuthenticationService(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory) : IReAuthenticationService
{
    public async Task<bool> VerifyPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(password);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return false;
        }

        return user.PasswordHash is { } hash
            && userManager.PasswordHasher.VerifyHashedPassword(user, hash, password)
                is not PasswordVerificationResult.Failed;
    }

    public async Task<bool> VerifyTwoFactorCodeAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(code);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null || !await userManager.GetTwoFactorEnabledAsync(user))
        {
            return false;
        }

        if (await userManager.VerifyTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultAuthenticatorProvider,
                code))
        {
            return true;
        }

        return (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded;
    }
}
