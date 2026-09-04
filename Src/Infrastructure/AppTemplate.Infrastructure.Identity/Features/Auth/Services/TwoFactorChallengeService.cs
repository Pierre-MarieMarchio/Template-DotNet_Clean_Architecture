using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorChallenge;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using AppTemplate.Infrastructure.Identity.Common.Directories;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Services;

/// <summary>
/// <see cref="ITwoFactorChallengeService"/> over <see cref="UserManager{TUser}"/>'s authentication-token
/// store — the same <c>AspNetUserTokens</c> table ASP.NET Identity already uses for the authenticator
/// key and recovery codes, under a login provider name of this module's own so the two never collide.
/// <para>
/// <b>The challenge is server-held state, and has to stay that way.</b> It proves only that a
/// password was verified a moment ago, and it is checked against the stored row so that it can be
/// revoked on use; a self-describing bearer token would have neither property. The row also
/// survives a redeploy and is visible to every replica, which an in-memory cache would not be.
/// </para>
/// <para>
/// <b>Format.</b> The token handed to the caller is <c>{userId}.{secret}</c>: a login is anonymous,
/// so the account has to be self-describing rather than found by an index this table does not offer.
/// Only <c>SHA-256(secret)</c> is stored, for the same reason <c>RefreshTokenGrantsService</c> stores only a
/// refresh token's hash. <c>SetAuthenticationTokenAsync</c> overwrites in place, so issuing a new
/// challenge for an account supersedes whichever one came before it — there is never more than one
/// live challenge per account for an attacker to choose between.
/// </para>
/// </summary>
internal sealed class TwoFactorChallengeService(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory,
    IDateTimeProvider dateTimeProvider,
    IOptions<TwoFactorOptions> options) : ITwoFactorChallengeService
{
    private const string _loginProvider = "TwoFactorChallenge";
    private const string _tokenName = "PendingLogin";

    /// <summary>256 bits of entropy, for the reason <c>RefreshTokenGrantsService</c> gives for its own secret.</summary>
    private const int _secretSizeInBytes = 32;

    public async Task<IssuedTwoFactorChallenge> IssueAsync(Guid userId, CancellationToken cancellationToken = default)
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

    public async Task<TwoFactorRedemptionOutcome> RedeemAsync(
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

        // Belt and braces, and deliberately not the primary bound: the write below removes a
        // challenge as it reaches the ceiling, so a stored value that still carries a maxed-out count
        // is one whose removal did not happen — a process that stopped between the two writes, or an
        // operator who edited the row. Refused without looking at the code, because the one thing
        // this path must never do is answer one more guess.
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
            // The challenge stays live for a mistyped code — forcing the caller back through /login
            // for a password they proved a moment ago is a cost paid by the owner, not the attacker —
            // but it stays live a bounded number of times. Account lockout counts failed password
            // checks and a code is not one, so this counter is the only thing between somebody
            // holding the password and the whole six-digit space.
            int spent = attempts + 1;

            if (spent >= options.Value.MaxChallengeAttempts)
            {
                await userManager.RemoveAuthenticationTokenAsync(user, _loginProvider, _tokenName);
            }
            else
            {
                // Rewritten rather than incremented in place: SetAuthenticationTokenAsync overwrites,
                // and the expiry and hash are carried over verbatim so that spending an attempt can
                // never extend a challenge's life or change what it authorises.
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

    /// <summary>
    /// Reads either shape. A two-part value is one written before the attempt counter existed, and
    /// is read as having spent none: a challenge live across the deploy that added this is worth
    /// honouring, and the alternative — treating it as unparseable — would sign out everybody
    /// mid-login for no security gain.
    /// </summary>
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

        // An unparseable or negative count is read as the ceiling rather than as zero: the one thing
        // a corrupted counter must not do is hand back an unlimited number of guesses.
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
}
