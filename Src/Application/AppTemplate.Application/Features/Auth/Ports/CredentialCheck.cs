namespace AppTemplate.Application.Features.Auth.Ports;

/// <param name="Account">Present only when the credential was verified.</param>
public sealed record CredentialCheck(CredentialCheckOutcome Outcome, AccountIdentity? Account)
{
    public static CredentialCheck Verified(AccountIdentity account) =>
        new(CredentialCheckOutcome.Verified, account);

    public static CredentialCheck Refused(CredentialCheckOutcome outcome) => new(outcome, null);
}
