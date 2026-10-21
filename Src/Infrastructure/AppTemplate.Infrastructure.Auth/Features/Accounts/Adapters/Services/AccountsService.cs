using System.Globalization;
using System.Security.Cryptography;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;

internal sealed class AccountsService(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IAppUserDirectory directory,
    IDateTimeProvider dateTimeProvider) : IAccountsService
{
    private static string? _decoyPasswordHash;

    private string DecoyPasswordHash => LazyInitializer.EnsureInitialized(
        ref _decoyPasswordHash,
        () => userManager.PasswordHasher.HashPassword(
            new AppUser(),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    public async Task<AccountRegistrationOutcome> RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            CreatedAt = dateTimeProvider.UtcNow,
        };

        var refusals = await RefusalsOfTheSubmittedValuesAsync(user, password);

        if (refusals.Count > 0)
        {
            return Rejected(refusals);
        }

        var existing = await userManager.FindByEmailAsync(email);

        if (existing is not null)
        {
            userManager.PasswordHasher.HashPassword(existing, password);

            return AccountRegistrationOutcome.AlreadyRegistered(existing.EmailConfirmed);
        }

        IdentityResult result;

        try
        {
            result = await userManager.CreateAsync(user, password);
        }
        catch (DbUpdateException exception)
            when (DatabaseProvider.IsUniqueViolation(exception))
        {
            return await AlreadyRegisteredAfterALostRaceAsync(email);
        }

        if (result.Succeeded)
        {
            return AccountRegistrationOutcome.Created(user.Id);
        }

        return result.Errors.Any(IsDuplicate)
            ? await AlreadyRegisteredAfterALostRaceAsync(email)
            : Rejected([.. result.Errors]);
    }

    private async Task<List<IdentityError>> RefusalsOfTheSubmittedValuesAsync(AppUser candidate, string password)
    {
        var refusals = new List<IdentityError>();

        foreach (var validator in userManager.PasswordValidators)
        {
            refusals.AddRange((await validator.ValidateAsync(userManager, candidate, password)).Errors);
        }

        foreach (var validator in userManager.UserValidators)
        {
            refusals.AddRange((await validator.ValidateAsync(userManager, candidate)).Errors.Where(error => !IsDuplicate(error)));
        }

        return refusals;
    }

    private async Task<AccountRegistrationOutcome> AlreadyRegisteredAfterALostRaceAsync(string email)
    {
        var winner = await userManager.FindByEmailAsync(email);

        return AccountRegistrationOutcome.AlreadyRegistered(winner?.EmailConfirmed ?? false);
    }

    private static bool IsDuplicate(IdentityError error) =>
        error.Code is nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.DuplicateEmail);

    private static AccountRegistrationOutcome Rejected(IReadOnlyCollection<IdentityError> refusals)
    {
        var byField = refusals.ToLookup(
            error => error.Code is nameof(IdentityErrorDescriber.InvalidUserName)
                or nameof(IdentityErrorDescriber.InvalidEmail),
            error => error.Description);

        return AccountRegistrationOutcome.Rejected(JoinOrNull(byField[true]), JoinOrNull(byField[false]));
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
            SpendADecoyKeyDerivation(new AppUser(), password);

            return CredentialCheckOutcome.Refused(CredentialCheckStatus.NoSuchAccount);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        var identity = new AccountIdentity(
            user.Id,
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

        if (outcome is CredentialCheckStatus.LockedOut or CredentialCheckStatus.EmailNotConfirmed)
        {
            SpendADecoyKeyDerivation(user, password);
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

        if (user is null)
        {
            return AccountDeletionOutcome.NoSuchAccount;
        }

        var result = await userManager.DeleteAsync(user);

        return result.Succeeded ? AccountDeletionOutcome.Deleted : AccountDeletionOutcome.Rejected;
    }

    private void SpendADecoyKeyDerivation(AppUser user, string password) =>
        userManager.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash ?? DecoyPasswordHash, password);

    private static string? JoinOrNull(IEnumerable<string> descriptions) =>
        descriptions.Any() ? string.Join(" ", descriptions) : null;
}
