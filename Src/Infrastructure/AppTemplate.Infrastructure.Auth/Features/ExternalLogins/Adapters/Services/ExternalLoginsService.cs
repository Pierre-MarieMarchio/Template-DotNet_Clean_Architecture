using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalLogins;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Adapters.Services;

/// <summary><see cref="IExternalLoginsService"/> over ASP.NET Identity's <c>UserLogins</c> table.
/// It translates and decides nothing: which of these four operations a sign-in calls is
/// <c>ExternalAccountLinkPolicy</c>'s decision, and every refusal here is reported as the store's
/// refusal rather than interpreted into one.</summary>
internal sealed class ExternalLoginsService(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory,
    IOptions<IdentityOptions> identityOptions,
    IDateTimeProvider dateTimeProvider) : IExternalLoginsService
{
    /// <summary>How many user names are tried before provisioning gives up. Three: the only reason to
    /// try again is that the derived name is taken, and a fourth collision is a store saying no for a
    /// reason more attempts will not change.</summary>
    private const int _userNameAttempts = 3;

    /// <summary>
    /// The random part appended to a taken user name. Six hex characters is thirty-two bits fewer
    /// than a full identifier and still leaves a collision on the second attempt effectively
    /// impossible, while keeping the name something a person can read back to support.
    /// </summary>
    private const int _suffixLength = 6;

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

        for (int attempt = 0; attempt < _userNameAttempts; attempt++)
        {
            string? userName = DeriveUserName(email, attempt);

            if (userName is null)
            {
                return null;
            }

            var user = new AppUser
            {
                UserName = userName,
                Email = email,

                // The provider verified the address, which ExternalAccountLinkPolicy checked before
                // this call. Left unconfirmed, the account would be created and then refused by
                // SignInManager.CanSignInAsync on every first sign-in.
                EmailConfirmed = true,

                CreatedAt = dateTimeProvider.UtcNow,
            };

            // No password overload: an account reached through a provider has no secret to invent.
            var created = await userManager.CreateAsync(user);

            if (created.Succeeded)
            {
                return await LinkNewAccountAsync(user, provider, subject);
            }

            // A taken user name is the one failure another candidate fixes. A taken address means
            // somebody claimed it between the caller's lookup and this call.
            if (!IsDuplicateUserName(created))
            {
                return null;
            }
        }

        return null;
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

    private static bool IsDuplicateUserName(IdentityResult result) =>
        result.Errors.Any(error => string.Equals(
            error.Code,
            nameof(IdentityErrorDescriber.DuplicateUserName),
            StringComparison.Ordinal));

    private static AccountIdentity ToAccountIdentity(AppUser user) =>
        new(user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty, user.TwoFactorEnabled);

    /// <summary>A user name built from the address, kept inside
    /// <c>IdentityOptions.User.AllowedUserNameCharacters</c> — not cosmetic: that setting is a hard
    /// gate in ASP.NET Identity's own <c>UserValidator</c>, and its default set is ASCII letters,
    /// digits and <c>-._@+</c>, so an address like <c>rené.dupont@example.com</c> would otherwise
    /// yield a name <c>CreateAsync</c> rejects outright. The allowed set is read from the options
    /// rather than assumed. Returns <c>null</c> when nothing survives — a name invented out of
    /// nothing would be worse than a refusal the operator can read in the set they configured.</summary>
    private string? DeriveUserName(string email, int attempt)
    {
        string allowed = identityOptions.Value.User.AllowedUserNameCharacters;

        int at = email.IndexOf('@', StringComparison.Ordinal);
        string candidate = Keep(at < 0 ? email : email[..at], allowed);

        // Nothing of the address survived the allowed set, so the name comes from an identifier:
        // hexadecimal digits are in every plausible allowed set and cannot be empty.
        if (candidate.Length == 0)
        {
            candidate = Keep(Guid.CreateVersion7().ToString("N"), allowed);
        }

        if (attempt > 0)
        {
            candidate += Keep(Guid.CreateVersion7().ToString("N")[.._suffixLength], allowed);
        }

        return candidate.Length == 0 ? null : candidate;
    }

    /// <summary>
    /// An empty allowed set means ASP.NET Identity applies no restriction at all, so an empty set
    /// here has to mean the same thing — filtering everything out would be the opposite reading.
    /// </summary>
    private static string Keep(string value, string allowed) =>
        string.IsNullOrEmpty(allowed)
            ? value
            : string.Concat(value.Where(character => allowed.Contains(character, StringComparison.Ordinal)));
}
