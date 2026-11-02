namespace AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;

public enum TwoFactorConfirmationStatus
{
    Confirmed,

    InvalidCode,

    IncorrectPassword,

    NoSuchAccount,

    AlreadyEnabled,

    Rejected,
}
