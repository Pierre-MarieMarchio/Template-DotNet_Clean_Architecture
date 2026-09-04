using AppTemplate.Application.Features.Auth.Ports.UserAccounts;

namespace AppTemplate.Application.Features.Auth.Ports.ExternalLogins;

/// <summary>
/// The link between a provider's account and a local one: the <c>UserLogins</c> table ASP.NET
/// Identity already maps and that nothing in this template has used until now.
/// <para>
/// Separate from <see cref="IUserAccountsService"/>, which is about a password: verifying one,
/// changing one, and creating an account that has one. Not one of those four operations is useful
/// here, and none of these four is useful there — an account reached through a provider has no
/// password at all. The two are also each exactly at the four-operation ceiling, so this is a
/// capability that could not have been added to that port whatever the reading.
/// </para>
/// <para>
/// It deliberately does <b>not</b> decide anything. Reading, linking and provisioning are four
/// primitives; which of them a given sign-in calls is
/// <see cref="AppTemplate.Application.Features.Auth.Policies.ExternalAccountLinkPolicy"/>'s decision,
/// because that decision is the security-relevant part and belongs where a unit test can enumerate
/// its cases.
/// </para>
/// <para>
/// <b>Cancellation is observed on entry only</b>, for the reason given on
/// <see cref="IUserAccountsService"/>.
/// </para>
/// </summary>
public interface IExternalLoginsService
{
    /// <summary>
    /// The account already linked to this provider identity, or <c>null</c>.
    /// <para>
    /// Keyed on the pair and never on an address. The subject is the only claim a provider promises
    /// is stable, and Apple sends no address after the first authorisation, so a lookup by address
    /// would return nothing on a user's second sign-in — in production, silently, and never in a
    /// development account that has only ever authorised once.
    /// </para>
    /// </summary>
    Task<AccountIdentity?> FindByExternalLoginAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The local account holding this address, with whether that address was ever confirmed.
    /// <para>
    /// Reached only when the provider vouched for the address, and only on a first link. The
    /// confirmation flag is the whole point of the call: an account registered at an address nobody
    /// ever proved is exactly what must not be linked to.
    /// </para>
    /// </summary>
    /// <returns><c>null</c> when no account holds the address.</returns>
    Task<LocalAccountMatch?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a provider identity to an account that already exists.
    /// </summary>
    /// <returns>
    /// <see cref="ExternalLoginLinkStatus.Refused"/> when the store would not take it — including the
    /// race where another request linked the same pair between
    /// <see cref="FindByExternalLoginAsync"/> and this call, which the unique key on the pair is what
    /// actually prevents.
    /// </returns>
    Task<ExternalLoginLinkStatus> LinkAsync(
        Guid userId,
        string provider,
        string subject,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an account for an address no local account holds and links the provider identity to it,
    /// as one step.
    /// <para>
    /// The account has <b>no password</b> — there is no secret to invent and none the user could ever
    /// be told — and its address is stored <b>already confirmed</b>, because the provider verified it
    /// and mailing a confirmation link to an address a third party just vouched for asks the user to
    /// prove something twice.
    /// </para>
    /// <para>
    /// The store chooses the user name and reports it back in
    /// <see cref="ExternalAccountProvisionOutcome.Account"/>: the provider supplied an address and a
    /// subject, and inventing a display name from either is the user store's business, not a use
    /// case's.
    /// </para>
    /// </summary>
    Task<ExternalAccountProvisionOutcome> ProvisionAsync(
        string email,
        string provider,
        string subject,
        CancellationToken cancellationToken = default);
}
