using System.Security.Cryptography;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Users;

/// <summary>
/// <see cref="IUserAccounts"/> over <see cref="UserManager{TUser}"/> and
/// <see cref="SignInManager{TUser}"/>. It translates and nothing more: every decision about what a
/// refusal means to a caller belongs to the use case.
/// </summary>
internal sealed class UserAccounts(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IAppUserDirectory directory,
    IDateTimeProvider dateTimeProvider,
    ISecurityEventLog securityEventLog) : IUserAccounts
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

    public async Task<AccountCreation> CreateAsync(
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
            return AccountCreation.Created(user.Id);
        }

        bool isConflict = result.Errors.Any(error =>
            error.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));

        return isConflict
            ? AccountCreation.Conflict
            : AccountCreation.Rejected(string.Join(" ", result.Errors.Select(error => error.Description)));
    }

    public async Task<CredentialCheck> VerifyCredentialAsync(
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

            return CredentialCheck.Refused(CredentialCheckOutcome.NoSuchAccount);
        }

        // lockoutOnFailure is what bounds brute force: without it AccessFailedCount never moves and
        // password guessing is unlimited.
        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        var identity = new AccountIdentity(user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty);

        if (result.Succeeded)
        {
            return CredentialCheck.Verified(identity);
        }

        var outcome = result switch
        {
            { IsLockedOut: true } => CredentialCheckOutcome.LockedOut,
            { IsNotAllowed: true } => CredentialCheckOutcome.EmailNotConfirmed,
            _ => CredentialCheckOutcome.IncorrectPassword,
        };

        // CheckPasswordSignInAsync runs its lockout/confirmation check before deriving a key, so a
        // locked-out or unconfirmed account answers without paying for PBKDF2 while a wrong password
        // does. Burn the same key derivation here, result ignored, so the two refusals cost the same.
        // Not userManager.CheckPasswordAsync: it rewrites the stored hash on a rehash-needed result,
        // which would rotate the security stamp on a login that was just refused.
        if (outcome is CredentialCheckOutcome.LockedOut or CredentialCheckOutcome.EmailNotConfirmed)
        {
            userManager.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash ?? AbsentUserPasswordHash, password);
        }

        // The lockout threshold itself is Identity's own state machine; nothing outside this adapter
        // can see it cross, so recording it here is not a decision moved out of the use case, it is
        // one the use case has no way to make.
        if (outcome is CredentialCheckOutcome.LockedOut)
        {
            securityEventLog.Record(SecurityEvent.AccountLockedOut(user.Id));
        }

        return CredentialCheck.Refused(outcome, identity);
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

    public async Task<PasswordChange> ChangePasswordAsync(
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
            return PasswordChange.IncorrectCurrentPassword;
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);

        if (result.Succeeded)
        {
            return PasswordChange.Changed;
        }

        bool wrongCurrentPassword = result.Errors.Any(error =>
            string.Equals(error.Code, "PasswordMismatch", StringComparison.Ordinal));

        return wrongCurrentPassword
            ? PasswordChange.IncorrectCurrentPassword
            : PasswordChange.Rejected(string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}
