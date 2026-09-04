using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Auth.Ports.ExternalLogins;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using AppTemplate.Infrastructure.Identity.Common.Directories;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Services;

/// <summary>
/// <see cref="IExternalLoginsService"/> over ASP.NET Identity's <c>UserLogins</c> table — mapped
/// since the schema was first written and, until now, never read.
/// <para>
/// It translates and decides nothing. Which of these four operations a sign-in calls is
/// <c>ExternalAccountLinkPolicy</c>'s decision, and every refusal here is reported as the store's
/// refusal rather than interpreted into one.
/// </para>
/// </summary>
internal sealed class ExternalLoginsService(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory,
    IOptions<IdentityOptions> identityOptions,
    IDateTimeProvider dateTimeProvider) : IExternalLoginsService
{
    /// <summary>
    /// How many user names are tried before provisioning gives up. Three, because the only reason to
    /// try again is that the derived name is taken, and the second and third candidates carry random
    /// suffixes — a fourth collision is not a name clash any more, it is a store saying no for a
    /// reason more attempts will not change.
    /// </summary>
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

    public async Task<ExternalLoginLinkStatus> LinkAsync(
        Guid userId,
        string provider,
        string subject,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return ExternalLoginLinkStatus.Refused;
        }

        var result = await userManager.AddLoginAsync(user, new UserLoginInfo(provider, subject, provider));

        return result.Succeeded ? ExternalLoginLinkStatus.Linked : ExternalLoginLinkStatus.Refused;
    }

    public async Task<ExternalAccountProvisionOutcome> ProvisionAsync(
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
                return ExternalAccountProvisionOutcome.Refused;
            }

            var user = new AppUser
            {
                UserName = userName,
                Email = email,

                // The whole point of this flag being set here. The provider verified the address —
                // that is the precondition ExternalAccountLinkPolicy checked before allowing this
                // call — so an account left unconfirmed would be created successfully and then
                // refused at the very next step by SignInManager.CanSignInAsync, on every single
                // first sign-in, invisibly to anything but a run against a real store.
                EmailConfirmed = true,

                CreatedAt = dateTimeProvider.UtcNow,
            };

            // No password overload: an account reached through a provider has no secret to invent
            // and none the user could ever be told.
            var created = await userManager.CreateAsync(user);

            if (created.Succeeded)
            {
                return await LinkNewAccountAsync(user, provider, subject);
            }

            // A taken user name is the one failure another candidate fixes. A taken address is not:
            // somebody claimed it between the caller's lookup and this call, and that is exactly
            // what ExternalAccountProvisionStatus.Refused describes.
            if (!IsDuplicateUserName(created))
            {
                return ExternalAccountProvisionOutcome.Refused;
            }
        }

        return ExternalAccountProvisionOutcome.Refused;
    }

    /// <summary>
    /// The second half of the one step the port promises. There is no transaction across the two
    /// calls, so a failed link is compensated rather than left behind: without the deletion, a
    /// failure here leaves an account holding the address, carrying no password and attached to no
    /// provider — one nobody can ever sign into and which blocks the address for good.
    /// </summary>
    private async Task<ExternalAccountProvisionOutcome> LinkNewAccountAsync(
        AppUser user,
        string provider,
        string subject)
    {
        var linked = await userManager.AddLoginAsync(user, new UserLoginInfo(provider, subject, provider));

        if (!linked.Succeeded)
        {
            await userManager.DeleteAsync(user);

            return ExternalAccountProvisionOutcome.Refused;
        }

        return ExternalAccountProvisionOutcome.Provisioned(ToAccountIdentity(user));
    }

    private static bool IsDuplicateUserName(IdentityResult result) =>
        result.Errors.Any(error => string.Equals(
            error.Code,
            nameof(IdentityErrorDescriber.DuplicateUserName),
            StringComparison.Ordinal));

    private static AccountIdentity ToAccountIdentity(AppUser user) =>
        new(user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty, user.TwoFactorEnabled);

    /// <summary>
    /// A user name built from the address, kept inside <c>IdentityOptions.User.AllowedUserNameCharacters</c>.
    /// <para>
    /// The filtering is not cosmetic. That setting is a hard gate in ASP.NET Identity's own
    /// <c>UserValidator</c>, and its default set is ASCII letters, digits and <c>-._@+</c> — so an
    /// address as ordinary as <c>rené.dupont@example.com</c> yields a name <c>CreateAsync</c>
    /// rejects outright, and the use case above it would report a refusal it could not explain. An
    /// installation that narrowed the set further would break more addresses, not fewer, which is
    /// why the allowed set is read from the options rather than assumed.
    /// </para>
    /// <para>
    /// Returns <c>null</c> when nothing survives — an address written entirely in characters this
    /// installation forbids — because a name invented out of nothing at all would be worse than a
    /// refusal the operator can read in the allowed set they configured.
    /// </para>
    /// </summary>
    private string? DeriveUserName(string email, int attempt)
    {
        string allowed = identityOptions.Value.User.AllowedUserNameCharacters;

        int at = email.IndexOf('@', StringComparison.Ordinal);
        string candidate = Keep(at < 0 ? email : email[..at], allowed);

        // Nothing of the address survived the allowed set, so the name comes from an identifier
        // instead. Its hexadecimal digits are in every plausible allowed set, and unlike the address
        // it cannot be empty.
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
