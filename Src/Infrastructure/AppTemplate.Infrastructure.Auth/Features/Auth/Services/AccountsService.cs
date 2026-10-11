using System.Globalization;
using System.Security.Cryptography;
using AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Directories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Services;

/// <summary>
/// <see cref="IAccountsService"/> over <see cref="UserManager{TUser}"/> and
/// <see cref="SignInManager{TUser}"/>. It translates and nothing more: every decision about what a
/// refusal means to a caller belongs to the use case.
/// </summary>
internal sealed class AccountsService(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IAppUserDirectory directory,
    IDateTimeProvider dateTimeProvider) : IAccountsService
{
    private static string? _absentUserPasswordHash;

    /// <summary>
    /// A real hash of a random string, verified when the email is unknown, so that the "no such
    /// user" path does the same key derivation the real one does and the response time does not tell
    /// an attacker which addresses are registered.
    /// <para>
    /// Produced by the <em>configured</em> hasher, so the decoy and the real verification derive a key
    /// the same number of times. Building it from a fresh <see cref="PasswordHasher{TUser}"/> would
    /// take that hasher's default iteration count and compatibility mode, and the two paths would stop
    /// costing the same as soon as either was configured away from the default. Computed once per
    /// process, since a key derivation per request is exactly the latency this is meant to hide.
    /// </para>
    /// </summary>
    private string AbsentUserPasswordHash => LazyInitializer.EnsureInitialized(
        ref _absentUserPasswordHash,
        () => userManager.PasswordHasher.HashPassword(
            new AppUser(),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    public async Task<AccountCreationOutcome> CreateAsync(
        string userName,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new AppUser
        {
            UserName = userName,
            Email = email,
            CreatedAt = dateTimeProvider.UtcNow,
        };

        var result = await userManager.CreateAsync(user, password);

        if (result.Succeeded)
        {
            return AccountCreationOutcome.Created(user.Id);
        }

        bool isConflict = result.Errors.Any(error =>
            error.Code is nameof(IdentityErrorDescriber.DuplicateUserName)
                or nameof(IdentityErrorDescriber.DuplicateEmail));

        if (isConflict)
        {
            return AccountCreationOutcome.Conflict;
        }

        // Identity's password codes, and any code a derived project's own validator adds, stay on the
        // password: the only value left that the store judges.
        var refusals = result.Errors.ToLookup(
            error => error.Code switch
            {
                nameof(IdentityErrorDescriber.InvalidUserName) => nameof(AppUser.UserName),
                nameof(IdentityErrorDescriber.InvalidEmail) => nameof(AppUser.Email),
                _ => nameof(password),
            },
            error => error.Description,
            StringComparer.Ordinal);

        return AccountCreationOutcome.Rejected(
            JoinOrNull(refusals[nameof(AppUser.UserName)]),
            JoinOrNull(refusals[nameof(AppUser.Email)]),
            JoinOrNull(refusals[nameof(password)]));
    }

    public async Task<CredentialCheckOutcome> VerifyCredentialAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            // Burn the same amount of CPU the real path would.
            userManager.PasswordHasher.VerifyHashedPassword(new AppUser(), AbsentUserPasswordHash, password);

            return CredentialCheckOutcome.Refused(CredentialCheckStatus.NoSuchAccount);
        }

        // lockoutOnFailure is what bounds brute force: without it AccessFailedCount never moves.
        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        var identity = new AccountIdentity(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            user.TwoFactorEnabled);

        if (result.Succeeded)
        {
            return CredentialCheckOutcome.Verified(identity);
        }

        var outcome = result switch
        {
            { IsLockedOut: true } => CredentialCheckStatus.LockedOut,
            { IsNotAllowed: true } => CredentialCheckStatus.EmailNotConfirmed,
            _ => CredentialCheckStatus.IncorrectPassword,
        };

        // CheckPasswordSignInAsync checks lockout and confirmation before deriving a key, so those
        // refusals would answer faster than a wrong password. Burn the same derivation, result
        // ignored. Not CheckPasswordAsync: it would rotate the security stamp on a refused login.
        if (outcome is CredentialCheckStatus.LockedOut or CredentialCheckStatus.EmailNotConfirmed)
        {
            userManager.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash ?? AbsentUserPasswordHash, password);
        }

        return CredentialCheckOutcome.Refused(outcome, identity);
    }

    public async Task<bool> CanSignInAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return false;
        }

        return !await userManager.IsLockedOutAsync(user) && await signInManager.CanSignInAsync(user);
    }

    public async Task<PasswordChangeOutcome> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        // The caller already authenticated as this id, so there is no address to protect from
        // enumeration here: an absent account only means it was deleted after the token was issued.
        if (user is null)
        {
            return PasswordChangeOutcome.NoSuchAccount;
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);

        if (result.Succeeded)
        {
            return PasswordChangeOutcome.Changed;
        }

        bool wrongCurrentPassword = result.Errors.Any(error =>
            string.Equals(error.Code, "PasswordMismatch", StringComparison.Ordinal));

        return wrongCurrentPassword
            ? PasswordChangeOutcome.IncorrectCurrentPassword
            : PasswordChangeOutcome.Rejected(string.Join(" ", result.Errors.Select(error => error.Description)));
    }

    public async Task<UserProfile?> FindProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString("D", CultureInfo.InvariantCulture));

        if (user is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);

        return new UserProfile(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            [.. roles],
            user.CreatedAt,
            user.TwoFactorEnabled,
            user.PasswordHash is not null);
    }

    public async Task<AccountDeletionOutcome> DeleteAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        // The caller already authenticated as this id, so there is no address to protect from
        // enumeration here: an absent account only means it was deleted after the token was issued.
        if (user is null)
        {
            return AccountDeletionOutcome.NoSuchAccount;
        }

        var result = await userManager.DeleteAsync(user);

        return result.Succeeded ? AccountDeletionOutcome.Deleted : AccountDeletionOutcome.Rejected;
    }

    private static string? JoinOrNull(IEnumerable<string> descriptions) =>
        descriptions.Any() ? string.Join(" ", descriptions) : null;
}
