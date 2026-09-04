using AppTemplate.Application.Features.Auth.Ports.UserAccounts;

namespace AppTemplate.Application.Features.Auth.Ports.ExternalLogins;

/// <param name="Account">
/// Set only for <see cref="ExternalAccountProvisionStatus.Provisioned"/>, and carrying the user name
/// the store chose rather than one the caller guessed.
/// </param>
public sealed record ExternalAccountProvisionOutcome(
    ExternalAccountProvisionStatus Status,
    AccountIdentity? Account)
{
    public static ExternalAccountProvisionOutcome Refused { get; } =
        new(ExternalAccountProvisionStatus.Refused, null);

    public static ExternalAccountProvisionOutcome Provisioned(AccountIdentity account) =>
        new(ExternalAccountProvisionStatus.Provisioned, account);
}
