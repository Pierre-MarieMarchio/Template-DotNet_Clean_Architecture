using AppTemplate.Application.Auth.Features.Auth.Ports.ReAuthentication;
using AppTemplate.Infrastructure.Auth.Common.Directories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Services;

/// <summary>
/// <see cref="IReAuthenticationService"/> over <see cref="UserManager{TUser}"/>. The two proofs it
/// verifies are the same plumbing <c>TwoFactorService</c> uses — the password through the hasher, a
/// code through the authenticator provider or a recovery code — with none of the sign-in state a
/// challenge carries: there is no challenge to park, because the caller already holds a valid
/// access token and only needs to prove it is the account's holder.
/// </summary>
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

        // Verified through the hasher, as TwoFactorService's confirmation and disable paths are, so
        // a needed rehash rotates no security stamp: proving the password must not end the very
        // session that is proving it.
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

        // The same fallback a sign-in challenge offers, spent the same way. A wrong code redeems
        // nothing: RedeemTwoFactorRecoveryCodeAsync only succeeds for one still on file.
        return (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded;
    }
}
