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

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return TwoFactorSetupOutcome.NoSuchAccount;
        }

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            return TwoFactorSetupOutcome.AlreadyEnabled;
        }

        string? key = await userManager.GetAuthenticatorKeyAsync(user);

        if (string.IsNullOrEmpty(key))
        {
            // Rotates the security stamp, as ASP.NET Identity's own implementation does.
            var provisioned = await userManager.ResetAuthenticatorKeyAsync(user);

            // A key the store did not keep would fail every code the caller's app then produces.
            if (!provisioned.Succeeded)
            {
                return TwoFactorSetupOutcome.Rejected;
            }

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

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return TwoFactorConfirmationOutcome.NoSuchAccount;
        }

        // Confirming again would replace the codes the caller holds before writing the switch-on,
        // and a refused write would leave codes nobody was shown.
        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            return TwoFactorConfirmationOutcome.AlreadyEnabled;
        }

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

        // Codes before arming: a refusal at either step then leaves the factor off, never armed with
        // no codes to recover by. Both fail only on a store-level conflict, never on the caller's input.
        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
            user,
            options.Value.RecoveryCodeCount);

        if (recoveryCodes is null)
        {
            return TwoFactorConfirmationOutcome.Rejected;
        }

        // Rotates the security stamp.
        var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);

        if (!enabled.Succeeded)
        {
            return TwoFactorConfirmationOutcome.Rejected;
        }

        return TwoFactorConfirmationOutcome.Confirmed([.. recoveryCodes]);
    }

    public async Task<TwoFactorDisableOutcome> DisableAsync(
        Guid userId,
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return TwoFactorDisableOutcome.NoSuchAccount;
        }

        // Verified through the hasher, as ConfirmAsync above is, to avoid CheckPasswordAsync's stamp
        // rotation on the way to refusing the request.
        if (userManager.PasswordHasher.VerifyHashedPassword(
                user, user.PasswordHash ?? string.Empty, currentPassword) == PasswordVerificationResult.Failed)
        {
            return TwoFactorDisableOutcome.IncorrectPassword;
        }

        return await TurnOffAsync(userManager, user)
            ? TwoFactorDisableOutcome.Disabled
            : TwoFactorDisableOutcome.Rejected;
    }

    /// <summary>
    /// Turns the second factor off and discards its secret: the one sequence both this service and
    /// <see cref="TwoFactorAdministrationService"/> run once their own proof is settled. On success the
    /// security stamp has rotated, whether the factor was armed or not.
    /// </summary>
    /// <returns><c>false</c> when the store refused to turn the factor off, which leaves it on.</returns>
    internal static async Task<bool> TurnOffAsync(UserManager<AppUser> userManager, AppUser user)
    {
        var disabled = await userManager.SetTwoFactorEnabledAsync(user, false);

        if (!disabled.Succeeded)
        {
            return false;
        }

        // Invalidates the secret, so a later re-enrollment does not reuse the key every authenticator
        // app on file already knows. Its refusal is not reported: the factor is already off, and the
        // caller must still revoke the sessions that change outdates.
        await userManager.ResetAuthenticatorKeyAsync(user);

        return true;
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
