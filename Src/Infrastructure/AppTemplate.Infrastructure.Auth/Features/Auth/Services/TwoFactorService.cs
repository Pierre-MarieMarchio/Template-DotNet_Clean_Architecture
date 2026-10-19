using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Directories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using AppTemplate.Infrastructure.Auth.Features.Auth.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Services;

/// <summary><see cref="ITwoFactorService"/> over <see cref="UserManager{TUser}"/>. Enrollment uses
/// ASP.NET Identity's own authenticator plumbing rather than RFC 6238 by hand — the shared secret,
/// the 30-second step and the SHA-1 HMAC are what
/// <see cref="TokenOptions.DefaultAuthenticatorProvider"/> already does. <b>The sign-in challenge is
/// server-held state, and has to stay that way:</b> it proves only that a password was verified a
/// moment ago and can be revoked on use, which a self-describing bearer token could not be. The row
/// lives in the same <c>AspNetUserTokens</c> table Identity uses for the authenticator key, under a
/// login provider name of this module's own, so it survives a redeploy and is visible to every
/// replica. The token handed to the caller is <c>{userId}.{secret}</c> — a login is anonymous, so the
/// account is self-describing — and only <c>SHA-256(secret)</c> is stored; issuing a new challenge
/// supersedes the previous one in place.</summary>
internal sealed class TwoFactorService(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory,
    IDateTimeProvider dateTimeProvider,
    IOptions<TwoFactorOptions> options) : ITwoFactorService
{
    private const string _loginProvider = "TwoFactorChallenge";
    private const string _tokenName = "PendingLogin";

    /// <summary>256 bits of entropy, for the reason <c>SessionTokensService</c> gives for its own secret.</summary>
    private const int _secretSizeInBytes = 32;

    public async Task<TwoFactorSetupOutcome> BeginSetupAsync(Guid userId, CancellationToken cancellationToken = default)
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

    public async Task<TwoFactorConfirmationOutcome> ConfirmSetupAsync(
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
        // cost the caller every session it holds except the one it is calling from. VerifyHashedPassword
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

    public async Task<TwoFactorDisableStatus> DisableAsync(
        Guid userId,
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return TwoFactorDisableStatus.NoSuchAccount;
        }

        // Verified through the hasher, as ConfirmSetupAsync above is, to avoid CheckPasswordAsync's stamp
        // rotation on the way to refusing the request.
        if (userManager.PasswordHasher.VerifyHashedPassword(
                user, user.PasswordHash ?? string.Empty, currentPassword) == PasswordVerificationResult.Failed)
        {
            return TwoFactorDisableStatus.IncorrectPassword;
        }

        return await TurnOffAsync(userManager, user)
            ? TwoFactorDisableStatus.Disabled
            : TwoFactorDisableStatus.Rejected;
    }

    /// <summary>Turns the second factor off and discards its secret: the one sequence both this
    /// service and <see cref="AccountAdministrationService"/> run once their own proof is settled.
    /// On success the security stamp has rotated, armed or not.</summary>
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

    public async Task<IssuedTwoFactorChallenge> IssueChallengeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"No account with id '{userId}' exists.");

        string secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(_secretSizeInBytes));
        var expiresAt = dateTimeProvider.UtcNow.Add(options.Value.ChallengeLifetime);

        await userManager.SetAuthenticationTokenAsync(
            user,
            _loginProvider,
            _tokenName,
            Encode(expiresAt, ComputeHash(secret), attempts: 0));

        return new IssuedTwoFactorChallenge($"{userId:N}.{secret}", expiresAt);
    }

    public async Task<TwoFactorRedemptionOutcome> RedeemChallengeAsync(
        string challengeToken,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challengeToken);
        ArgumentNullException.ThrowIfNull(code);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryParse(challengeToken, out var userId, out string presentedSecret))
        {
            return TwoFactorRedemptionOutcome.InvalidChallenge;
        }

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return TwoFactorRedemptionOutcome.InvalidChallenge;
        }

        string? stored = await userManager.GetAuthenticationTokenAsync(user, _loginProvider, _tokenName);

        if (stored is null
            || !TryDecode(stored, out var expiresAt, out byte[] expectedHash, out int attempts)
            || dateTimeProvider.UtcNow >= expiresAt
            || !CryptographicOperations.FixedTimeEquals(ComputeHash(presentedSecret), expectedHash))
        {
            return TwoFactorRedemptionOutcome.InvalidChallenge;
        }

        // Not the primary bound: the write below removes a challenge as it reaches the ceiling, so a
        // maxed-out count means that removal did not happen. Refused without looking at the code.
        if (attempts >= options.Value.MaxChallengeAttempts)
        {
            await userManager.RemoveAuthenticationTokenAsync(user, _loginProvider, _tokenName);

            return TwoFactorRedemptionOutcome.InvalidChallenge;
        }

        var account = ToAccountIdentity(user);

        bool fromAuthenticatorApp = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            code);

        if (fromAuthenticatorApp)
        {
            await userManager.RemoveAuthenticationTokenAsync(user, _loginProvider, _tokenName);
            return TwoFactorRedemptionOutcome.Verified(account, usedRecoveryCode: false);
        }

        var recovery = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);

        if (!recovery.Succeeded)
        {
            // The challenge survives a mistyped code, a bounded number of times. Account lockout
            // counts failed password checks and a code is not one, so this counter is the only thing
            // between somebody holding the password and the whole six-digit space.
            int spent = attempts + 1;

            if (spent >= options.Value.MaxChallengeAttempts)
            {
                await userManager.RemoveAuthenticationTokenAsync(user, _loginProvider, _tokenName);
            }
            else
            {
                // SetAuthenticationTokenAsync overwrites, so the expiry and hash are carried over
                // verbatim: spending an attempt must not extend the challenge or change what it
                // authorises.
                await userManager.SetAuthenticationTokenAsync(
                    user,
                    _loginProvider,
                    _tokenName,
                    Encode(expiresAt, expectedHash, spent));
            }

            return TwoFactorRedemptionOutcome.InvalidCode(account);
        }

        await userManager.RemoveAuthenticationTokenAsync(user, _loginProvider, _tokenName);
        return TwoFactorRedemptionOutcome.Verified(account, usedRecoveryCode: true);
    }

    private static AccountIdentity ToAccountIdentity(AppUser user) =>
        new(user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty, user.TwoFactorEnabled);

    private static bool TryParse(string challengeToken, out Guid userId, out string secret)
    {
        int separator = challengeToken.IndexOf('.', StringComparison.Ordinal);

        if (separator < 0
            || !Guid.TryParseExact(challengeToken[..separator], "N", out userId))
        {
            userId = Guid.Empty;
            secret = string.Empty;
            return false;
        }

        secret = challengeToken[(separator + 1)..];
        return secret.Length > 0;
    }

    /// <summary>
    /// <c>{expiry}:{hash}:{attempts}</c>. The count is stored beside the hash rather than in a column
    /// of its own because this row is ASP.NET Identity's token store, whose shape is not this
    /// module's to change.
    /// </summary>
    private static string Encode(DateTimeOffset expiresAt, byte[] hash, int attempts) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{expiresAt.ToUnixTimeSeconds()}:{Base64Url.EncodeToString(hash)}:{attempts}");

    /// <summary>Reads either shape. A two-part value predates the attempt counter and is read as
    /// having spent none: a challenge live across the deploy that added this is worth honouring, and
    /// treating it as unparseable would sign out everybody mid-login for no security gain.</summary>
    private static bool TryDecode(string stored, out DateTimeOffset expiresAt, out byte[] hash, out int attempts)
    {
        expiresAt = default;
        hash = [];
        attempts = 0;

        string[] parts = stored.Split(':');

        if (parts.Length is < 2 or > 3
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long unixSeconds)
            || !Base64Url.IsValid(parts[1]))
        {
            return false;
        }

        // An unparseable or negative count reads as the ceiling, never as zero: a corrupted counter
        // must not hand back unlimited guesses.
        if (parts.Length == 3
            && (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out attempts) || attempts < 0))
        {
            attempts = int.MaxValue;
        }

        expiresAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        hash = Base64Url.DecodeFromChars(parts[1]);
        return true;
    }

    private static byte[] ComputeHash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

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
