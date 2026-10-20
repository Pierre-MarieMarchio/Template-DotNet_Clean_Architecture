namespace AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;

public enum EmailChangeRequestStatus
{
    IncorrectCurrentPassword,

    Requested,

    AddressAlreadyRegistered,

    NoSuchAccount,
}
