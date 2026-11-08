namespace AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;

public enum TwoFactorDisableStatus
{
    Disabled,

    IncorrectPassword,

    Rejected,

    NoSuchAccount,
}
