using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorEnrollment;
using AppTemplate.Infrastructure.Auth.Common.Directories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using AppTemplate.Infrastructure.Auth.Features.Auth.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Services;

/// <summary>
/// <see cref="ITwoFactorEnrollmentService"/> over <see cref="UserManager{TUser}"/>. Every operation here
/// uses ASP.NET Identity's own authenticator plumbing — <c>GetAuthenticatorKeyAsync</c>,
/// <c>ResetAuthenticatorKeyAsync</c>, <c>VerifyTwoFactorTokenAsync</c>,
/// <c>GenerateNewTwoFactorRecoveryCodesAsync</c> — rather than RFC 6238 implemented by hand: the
/// shared secret, the 30-second step and the SHA-1 HMAC are exactly what
/// <see cref="TokenOptions.DefaultAuthenticatorProvider"/> already does, tested by the framework.
/// </summary>
internal sealed class TwoFactorEnrollmentService(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory,
    IOptions<TwoFactorOptions> options) : ITwoFactorEnrollmentService
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
            // Rotates the security stamp, as ASP.NET Identity's own implementation does.
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

        // Not CheckPasswordAsync: a rehash-needed result rotates the security stamp, which would
        // cost the caller every session it holds on a password that was correct. VerifyHashedPassword
        // has no such side effect.
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

        // Rotates the security stamp.
        var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);

        if (!enabled.Succeeded)
        {
            // Fails only on a store-level conflict, never on anything the caller submitted.
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
        // enumeration: an absent account only means it was deleted after the token was issued.
        // Verified through the hasher, as ConfirmAsync above is, to avoid CheckPasswordAsync's stamp
        // rotation on the way to refusing the request.
        if (user is null
            || userManager.PasswordHasher.VerifyHashedPassword(
                user, user.PasswordHash ?? string.Empty, currentPassword) == PasswordVerificationResult.Failed)
        {
            return TwoFactorDisableOutcome.IncorrectPassword;
        }

        // Rotates the security stamp.
        await userManager.SetTwoFactorEnabledAsync(user, false);

        // Invalidates the secret, so a later re-enrollment does not reuse the key every authenticator
        // app on file already knows.
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
