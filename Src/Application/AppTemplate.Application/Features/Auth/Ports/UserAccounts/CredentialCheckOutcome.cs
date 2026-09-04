namespace AppTemplate.Application.Features.Auth.Ports.UserAccounts;

/// <param name="Account">
/// Present when the credential was verified, and optionally on a refusal against a known account —
/// so the caller can attribute the failure to a user id in an audit trail without ever being handed
/// the address that was typed.
/// </param>
public sealed record CredentialCheckOutcome(CredentialCheckStatus Status, AccountIdentity? Account)
{
    public static CredentialCheckOutcome Verified(AccountIdentity account) =>
        new(CredentialCheckStatus.Verified, account);

    public static CredentialCheckOutcome Refused(CredentialCheckStatus outcome, AccountIdentity? account = null) =>
        new(outcome, account);
}
