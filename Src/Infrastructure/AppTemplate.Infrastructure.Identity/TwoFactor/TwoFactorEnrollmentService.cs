using AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;
using AppTemplate.Infrastructure.Identity.Options;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Users;

/// <summary>
/// <see cref="ITwoFactorEnrollment"/> over <see cref="UserManager{TUser}"/>. Every operation here
/// uses ASP.NET Identity's own authenticator plumbing — <c>GetAuthenticatorKeyAsync</c>,
/// <c>ResetAuthenticatorKeyAsync</c>, <c>VerifyTwoFactorTokenAsync</c>,
/// <c>GenerateNewTwoFactorRecoveryCodesAsync</c> — rather than RFC 6238 implemented by hand: the
/// shared secret, the 30-second step and the SHA-1 HMAC are exactly what
/// <see cref="TokenOptions.DefaultAuthenticatorProvider"/> already does, tested by the framework.
/// </summary>
internal sealed class TwoFactorEnrollment(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory,
    IOptions<TwoFactorOptions> options) : ITwoFactorEnrollment
{
    public async Task<TwoFactorSetupOutcome> BeginAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"No account with id '{userId}' exists.");

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            return TwoFactorSetupOutcome.AlreadyEnabled;
        }

        string? key = await userManager.GetAuthenticatorKeyAsync(user);

        if (string.IsNullOrEmpty(key))
        {
            // Rotates the security stamp as a side effect of ASP.NET Identity's own implementation —
            // see SetUpTwoFactorUseCase for why the use case does not compensate for that with a full
            // session wipe.
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        return TwoFactorSetupOutcome.Started(key!, BuildAuthenticatorUri(user.Email ?? string.Empty, key!));
    }

    public async Task<TwoFactorConfirmationOutcome> ConfirmAsync(
        Guid userId,
        string currentPassword,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(code);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"No account with id '{userId}' exists.");

        // Not userManager.CheckPasswordAsync: see EmailChangeTokens.IssueAsync for why. A
        // rehash-needed result would rotate the security stamp on its own, before the code below even
        // gets a say — which would arm nothing yet still cost the caller every session it holds, on a
        // password that was actually correct. VerifyHashedPassword alone has no such side effect.
        if (user.PasswordHash is not { } hash ||
            userManager.PasswordHasher.VerifyHashedPassword(user, hash, currentPassword)
                is PasswordVerificationResult.Failed)
        {
            return TwoFactorConfirmationOutcome.IncorrectPassword;
        }

        bool verified = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            code);

        if (!verified)
        {
            return TwoFactorConfirmationOutcome.InvalidCode;
        }

        // Rotates the security stamp — see ConfirmTwoFactorSetupUseCase for what that invalidates.
        var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);

        if (!enabled.Succeeded)
        {
            // Fails only on a store-level conflict, never on anything the caller submitted — there is
            // no more specific outcome to report than the code check already covers.
            return TwoFactorConfirmationOutcome.InvalidCode;
        }

        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
            user,
            options.Value.RecoveryCodeCount);

        return TwoFactorConfirmationOutcome.Confirmed([.. recoveryCodes ?? []]);
    }

    public async Task<TwoFactorDisableOutcome> DisableAsync(
        Guid userId,
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        // The caller already authenticated as this id, so there is no address to protect from
        // enumeration here — see IUserAccounts.ChangePasswordAsync for the same reasoning. An absent
        // account only means it was deleted after the token was issued.
        //
        // Verified through the hasher for the same reason ConfirmAsync above does it:
        // CheckPasswordAsync rewrites the stored hash on a rehash-needed result, which rotates the
        // security stamp — signing every device out on the way to *refusing* the request.
        if (user is null
            || userManager.PasswordHasher.VerifyHashedPassword(
                user, user.PasswordHash ?? string.Empty, currentPassword) == PasswordVerificationResult.Failed)
        {
            return TwoFactorDisableOutcome.IncorrectPassword;
        }

        // Rotates the security stamp — see DisableTwoFactorUseCase for what that invalidates.
        await userManager.SetTwoFactorEnabledAsync(user, false);

        // Invalidates the secret too, so a later re-enrollment starts from a fresh one instead of the
        // same key every authenticator app already on file for this account still knows.
        await userManager.ResetAuthenticatorKeyAsync(user);

        return TwoFactorDisableOutcome.Disabled;
    }

    /// <summary>
    /// The <c>otpauth://</c> URI a QR code encodes. The secret needs no escaping: Identity's
    /// authenticator key is base32, whose alphabet (<c>A-Z2-7</c>) is already URI-safe.
    /// </summary>
    private string BuildAuthenticatorUri(string email, string sharedKey)
    {
        string issuer = Uri.EscapeDataString(options.Value.Issuer);
        string label = Uri.EscapeDataString($"{options.Value.Issuer}:{email}");

        return $"otpauth://totp/{label}?secret={sharedKey}&issuer={issuer}&digits=6";
    }
}
