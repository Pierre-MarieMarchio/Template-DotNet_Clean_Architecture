namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

public enum CredentialCheckStatus
{
    Verified,

    NoSuchAccount,

    IncorrectPassword,

    EmailNotConfirmed,

    LockedOut,
}
