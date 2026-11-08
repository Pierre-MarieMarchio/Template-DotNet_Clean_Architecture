namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

public enum PasswordChangeStatus
{
    Changed,

    IncorrectCurrentPassword,

    Rejected,
    NoSuchAccount,
}
