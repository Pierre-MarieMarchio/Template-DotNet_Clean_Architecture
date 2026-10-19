using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

namespace AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalLogins;

/// <summary>The link between a provider's account and a local one, over the <c>UserLogins</c> table
/// ASP.NET Identity already maps. Separate from <see cref="IAccountsService"/>, which is about a
/// password. It deliberately decides nothing: reading, linking and provisioning are primitives, and
/// which a given sign-in calls is
/// <see cref="AppTemplate.Application.Auth.Features.ExternalLogins.Model.Policies.ExternalAccountLinkPolicy"/>'s
/// decision. Cancellation is observed on entry only (see <see cref="IAccountsService"/>).</summary>
public interface IExternalLoginsService
{
    /// <summary>The account already linked to this provider identity, or <c>null</c>. Keyed on the
    /// pair and never on an address: the subject is the only claim a provider promises is stable, and
    /// Apple sends no address after the first authorisation, so a lookup by address would return
    /// nothing on a user's second sign-in.</summary>
    Task<AccountIdentity?> FindByExternalLoginAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default);

    /// <summary>The local account holding this address, with whether that address was ever
    /// confirmed — the flag is the whole point: an account registered at an address nobody ever
    /// proved is exactly what must not be linked to. Reached only on a first link.</summary>
    /// <returns><c>null</c> when no account holds the address.</returns>
    Task<LocalAccountMatch?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Attaches a provider identity to an account that already exists.</summary>
    /// <returns>
    /// <c>false</c> when the store would not take it — including the race where another request
    /// linked the same pair in between, which the unique key on the pair is what actually prevents.
    /// </returns>
    Task<bool> LinkAsync(
        Guid userId,
        string provider,
        string subject,
        CancellationToken cancellationToken = default);

    /// <summary>Creates an account for an address no local account holds and links the provider
    /// identity to it, as one step. The account has <b>no password</b> and its address is stored
    /// <b>already confirmed</b> — the provider verified it. The store chooses the user name and
    /// reports it back.</summary>
    /// <returns>The new account, or <c>null</c> when the store refused to create or link it.</returns>
    Task<AccountIdentity?> ProvisionAsync(
        string email,
        string provider,
        string subject,
        CancellationToken cancellationToken = default);
}
