namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

public sealed record CredentialCheckOutcome(CredentialCheckStatus Status, AccountIdentity? Account)
{
    public static CredentialCheckOutcome Verified(AccountIdentity account) =>
        new(CredentialCheckStatus.Verified, account);

    public static CredentialCheckOutcome Refused(CredentialCheckStatus outcome, AccountIdentity? account = null) =>
        new(outcome, account);
}
