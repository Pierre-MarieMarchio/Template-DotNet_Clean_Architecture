using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalLogins;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Adapters.Services;

/// <summary><see cref="IExternalLoginsService"/> over ASP.NET Identity's <c>UserLogins</c> table.
/// It translates and decides nothing: which of these four operations a sign-in calls is
/// <c>ExternalAccountLinkPolicy</c>'s decision, and every refusal here is reported as the store's
/// refusal rather than interpreted into one.</summary>
internal sealed class ExternalLoginsService(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory,
    IDateTimeProvider dateTimeProvider) : IExternalLoginsService
{
    public async Task<AccountIdentity?> FindByExternalLoginAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByLoginAsync(provider, subject);

        return user is null ? null : ToAccountIdentity(user);
    }

    public async Task<LocalAccountMatch?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);

        return user is null ? null : new LocalAccountMatch(ToAccountIdentity(user), user.EmailConfirmed);
    }

    public async Task<bool> LinkAsync(
        Guid userId,
        string provider,
        string subject,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return false;
        }

        var result = await userManager.AddLoginAsync(user, new UserLoginInfo(provider, subject, provider));

        return result.Succeeded;
    }

    public async Task<AccountIdentity?> ProvisionAsync(
        string email,
        string provider,
        string subject,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            CreatedAt = dateTimeProvider.UtcNow,
        };

        var created = await userManager.CreateAsync(user);

        return created.Succeeded ? await LinkNewAccountAsync(user, provider, subject) : null;
    }

    /// <summary>The second half of the one step the port promises. No transaction spans the two
    /// calls, so a failed link is compensated: without the deletion it would leave an account holding
    /// the address, carrying no password and attached to no provider — one nobody can sign into and
    /// which blocks the address for good.</summary>
    private async Task<AccountIdentity?> LinkNewAccountAsync(
        AppUser user,
        string provider,
        string subject)
    {
        var linked = await userManager.AddLoginAsync(user, new UserLoginInfo(provider, subject, provider));

        if (!linked.Succeeded)
        {
            await userManager.DeleteAsync(user);

            return null;
        }

        return ToAccountIdentity(user);
    }

    private static AccountIdentity ToAccountIdentity(AppUser user) =>
        new(user.Id, user.Email ?? string.Empty, user.TwoFactorEnabled);
}
