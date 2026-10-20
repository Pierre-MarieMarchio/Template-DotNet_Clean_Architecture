using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.TwoFactor.Configuration.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.TwoFactor.Adapters.Services;

internal sealed class TwoFactorService(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory,
    IDateTimeProvider dateTimeProvider,
    IOptions<TwoFactorOptions> options) : ITwoFactorService
{
    private const string _loginProvider = "TwoFactorChallenge";
    private const string _tokenName = "PendingLogin";

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
            var provisioned = await userManager.ResetAuthenticatorKeyAsync(user);

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

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            return TwoFactorConfirmationOutcome.AlreadyEnabled;
        }

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

        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
            user,
            options.Value.RecoveryCodeCount);

        if (recoveryCodes is null)
        {
            return TwoFactorConfirmationOutcome.Rejected;
        }

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

        if (userManager.PasswordHasher.VerifyHashedPassword(
                user, user.PasswordHash ?? string.Empty, currentPassword) == PasswordVerificationResult.Failed)
        {
            return TwoFactorDisableStatus.IncorrectPassword;
        }

        return await TurnOffAsync(userManager, user)
            ? TwoFactorDisableStatus.Disabled
            : TwoFactorDisableStatus.Rejected;
    }

    internal static async Task<bool> TurnOffAsync(UserManager<AppUser> userManager, AppUser user)
    {
        var disabled = await userManager.SetTwoFactorEnabledAsync(user, false);

        if (!disabled.Succeeded)
        {
            return false;
        }

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
            int spent = attempts + 1;

            if (spent >= options.Value.MaxChallengeAttempts)
            {
                await userManager.RemoveAuthenticationTokenAsync(user, _loginProvider, _tokenName);
            }
            else
            {
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
        new(user.Id, user.Email ?? string.Empty, user.TwoFactorEnabled);

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

    private static string Encode(DateTimeOffset expiresAt, byte[] hash, int attempts) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{expiresAt.ToUnixTimeSeconds()}:{Base64Url.EncodeToString(hash)}:{attempts}");

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

    private string BuildAuthenticatorUri(string email, string sharedKey)
    {
        string issuer = Uri.EscapeDataString(options.Value.Issuer);
        string label = Uri.EscapeDataString($"{options.Value.Issuer}:{email}");

        return $"otpauth://totp/{label}?secret={sharedKey}&issuer={issuer}&digits=6";
    }
}
