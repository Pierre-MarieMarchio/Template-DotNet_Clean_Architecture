using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;

internal sealed class EmailTokensService(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory) : IEmailTokensService
{
    public async Task<string?> IssueEmailConfirmationAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);

        if (user is not { EmailConfirmed: false })
        {
            return null;
        }

        return await userManager.GenerateEmailConfirmationTokenAsync(user);
    }

    public async Task<bool> ConfirmEmailAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            return false;
        }

        var result = await userManager.ConfirmEmailAsync(user, token);

        if (!result.Succeeded)
        {
            return false;
        }

        await userManager.UpdateSecurityStampAsync(user);

        return true;
    }

    public async Task<string?> IssuePasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            return null;
        }

        return await userManager.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<PasswordResetOutcome> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(newPassword);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            return PasswordResetOutcome.InvalidToken;
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);

        if (result.Succeeded)
        {
            return PasswordResetOutcome.Succeeded(user.Id);
        }

        return IsInvalidToken(result)
            ? PasswordResetOutcome.InvalidToken
            : PasswordResetOutcome.Rejected(Describe(result));
    }

    public async Task<EmailChangeRequestOutcome> IssueEmailChangeAsync(
        Guid userId,
        string currentPassword,
        string newEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newEmail);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return EmailChangeRequestOutcome.NoSuchAccount;
        }

        if (user.PasswordHash is not { } hash ||
            userManager.PasswordHasher.VerifyHashedPassword(user, hash, currentPassword)
                is PasswordVerificationResult.Failed)
        {
            return EmailChangeRequestOutcome.IncorrectCurrentPassword;
        }

        var existing = await userManager.FindByEmailAsync(newEmail);

        if (existing is not null)
        {
            return EmailChangeRequestOutcome.AddressAlreadyRegistered;
        }

        return EmailChangeRequestOutcome.Issued(await userManager.GenerateChangeEmailTokenAsync(user, newEmail));
    }

    public async Task<EmailChangeOutcome> ChangeEmailAsync(
        Guid userId,
        string newEmail,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newEmail);
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return EmailChangeOutcome.InvalidToken;
        }

        string? previousUserName = user.UserName;
        user.UserName = newEmail;

        var result = await userManager.ChangeEmailAsync(user, newEmail, token);

        if (result.Succeeded)
        {
            return EmailChangeOutcome.Changed;
        }

        user.UserName = previousUserName;

        return IsInvalidToken(result)
            ? EmailChangeOutcome.InvalidToken
            : EmailChangeOutcome.Rejected(Describe(result));
    }

    private static bool IsInvalidToken(IdentityResult result) =>
        result.Errors.Any(error => string.Equals(error.Code, "InvalidToken", StringComparison.Ordinal));

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(error => error.Description));
}
