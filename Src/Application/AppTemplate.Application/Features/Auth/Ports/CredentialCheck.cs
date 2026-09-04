namespace AppTemplate.Application.Features.Auth.Ports;

/// <param name="Account">
/// Present when the credential was verified, and optionally on a refusal against a known account —
/// so the caller can attribute the failure to a user id in an audit trail without ever being handed
/// the address that was typed.
/// </param>
public sealed record CredentialCheck(CredentialCheckOutcome Outcome, AccountIdentity? Account)
{
    public static CredentialCheck Verified(AccountIdentity account) =>
        new(CredentialCheckOutcome.Verified, account);

    public static CredentialCheck Refused(CredentialCheckOutcome outcome, AccountIdentity? account = null) =>
        new(outcome, account);
}
