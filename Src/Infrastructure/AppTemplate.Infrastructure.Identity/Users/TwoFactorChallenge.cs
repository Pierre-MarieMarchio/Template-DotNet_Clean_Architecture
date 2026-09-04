using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorChallenge;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using AppTemplate.Infrastructure.Identity.Options;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Users;

/// <summary>
/// <see cref="ITwoFactorChallenge"/> over <see cref="UserManager{TUser}"/>'s authentication-token
/// store — the same <c>AspNetUserTokens</c> table ASP.NET Identity already uses for the authenticator
/// key and recovery codes, under a login provider name of this module's own so the two never collide.
/// <para>
/// <b>Why not a JWT or a data-protected token.</b> An access token proves a completed sign-in; this
/// proves only that a password was verified a moment ago and has to be checked against server-held
/// state to be revoked on use — a bearer token that decrypts itself would have neither property. A
/// row in the shared database also survives a redeploy and is visible to every replica, unlike an
/// in-memory cache would be.
/// </para>
/// <para>
/// <b>Format.</b> The token handed to the caller is <c>{userId}.{secret}</c>: a login is anonymous,
/// so the account has to be self-describing rather than found by an index this table does not offer.
/// Only <c>SHA-256(secret)</c> is stored, for the same reason <c>RefreshTokenGrants</c> stores only a
/// refresh token's hash. <c>SetAuthenticationTokenAsync</c> overwrites in place, so issuing a new
/// challenge for an account supersedes whichever one came before it — there is never more than one
/// live challenge per account for an attacker to choose between.
/// </para>
/// </summary>
internal sealed class TwoFactorChallenge(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory,
    IDateTimeProvider dateTimeProvider,
    IOptions<TwoFactorOptions> options) : ITwoFactorChallenge
{
    private const string _loginProvider = "TwoFactorChallenge";
    private const string _tokenName = "PendingLogin";

    /// <summary>256 bits of entropy, for the reason <c>RefreshTokenGrants</c> gives for its own secret.</summary>
    private const int _secretSizeInBytes = 32;

    public async Task<IssuedTwoFactorChallenge> IssueAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"No account with id '{userId}' exists.");

        string secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(_secretSizeInBytes));
        var expiresAt = dateTimeProvider.UtcNow.Add(options.Value.ChallengeLifetime);

        await userManager.SetAuthenticationTokenAsync(user, _loginProvider, _tokenName, Encode(expiresAt, secret));

        return new IssuedTwoFactorChallenge($"{userId:N}.{secret}", expiresAt);
    }

    public async Task<TwoFactorRedemption> RedeemAsync(
        string challengeToken,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challengeToken);
        ArgumentNullException.ThrowIfNull(code);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryParse(challengeToken, out var userId, out string presentedSecret))
        {
            return TwoFactorRedemption.InvalidChallenge;
        }

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return TwoFactorRedemption.InvalidChallenge;
        }

        string? stored = await userManager.GetAuthenticationTokenAsync(user, _loginProvider, _tokenName);

        if (stored is null
            || !TryDecode(stored, out var expiresAt, out byte[] expectedHash)
            || dateTimeProvider.UtcNow >= expiresAt
            || !CryptographicOperations.FixedTimeEquals(ComputeHash(presentedSecret), expectedHash))
        {
            return TwoFactorRedemption.InvalidChallenge;
        }

        var account = ToAccountIdentity(user);

        bool fromAuthenticatorApp = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            code);

        if (fromAuthenticatorApp)
        {
            await userManager.RemoveAuthenticationTokenAsync(user, _loginProvider, _tokenName);
            return TwoFactorRedemption.Verified(account, usedRecoveryCode: false);
        }

        var recovery = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);

        if (!recovery.Succeeded)
        {
            // The challenge stays live: a mistyped code should not force the caller back through
            // /login for a password it already proved once.
            return TwoFactorRedemption.InvalidCode(account);
        }

        await userManager.RemoveAuthenticationTokenAsync(user, _loginProvider, _tokenName);
        return TwoFactorRedemption.Verified(account, usedRecoveryCode: true);
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

    private static string Encode(DateTimeOffset expiresAt, string secret) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{expiresAt.ToUnixTimeSeconds()}:{Base64Url.EncodeToString(ComputeHash(secret))}");

    private static bool TryDecode(string stored, out DateTimeOffset expiresAt, out byte[] hash)
    {
        int separator = stored.IndexOf(':', StringComparison.Ordinal);

        if (separator < 0
            || !long.TryParse(stored.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out long unixSeconds)
            || !Base64Url.IsValid(stored.AsSpan(separator + 1)))
        {
            expiresAt = default;
            hash = [];
            return false;
        }

        expiresAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        hash = Base64Url.DecodeFromChars(stored.AsSpan(separator + 1));
        return true;
    }

    private static byte[] ComputeHash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));
}
