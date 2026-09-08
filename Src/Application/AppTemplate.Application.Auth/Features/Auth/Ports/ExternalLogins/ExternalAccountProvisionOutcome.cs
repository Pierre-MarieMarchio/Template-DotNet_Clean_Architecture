using AppTemplate.Application.Auth.Features.Auth.Ports.UserAccounts;

namespace AppTemplate.Application.Auth.Features.Auth.Ports.ExternalLogins;

/// <param name="Account">
/// Set only for <see cref="ExternalAccountProvisionStatus.Provisioned"/>, and carrying the user name
/// the store chose rather than one the caller guessed.
/// </param>
public sealed record ExternalAccountProvisionOutcome(
    ExternalAccountProvisionStatus Status,
    AccountIdentity? Account)
{
    /// <summary>
    /// Nothing was created, and there is nothing to hand back — see
    /// <see cref="ExternalAccountProvisionStatus.Refused"/>.
    /// </summary>
    public static ExternalAccountProvisionOutcome Refused { get; } =
        new(ExternalAccountProvisionStatus.Refused, null);

    /// <summary>An account now exists for the address with the provider identity attached to it.</summary>
    public static ExternalAccountProvisionOutcome Provisioned(AccountIdentity account) =>
        new(ExternalAccountProvisionStatus.Provisioned, account);
}
