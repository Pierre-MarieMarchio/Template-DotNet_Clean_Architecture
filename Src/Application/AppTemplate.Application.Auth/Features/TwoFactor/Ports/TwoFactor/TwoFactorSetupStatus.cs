namespace AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;

public enum TwoFactorSetupStatus
{
    Started,

    AlreadyEnabled,

    NoSuchAccount,

    Rejected,
}
