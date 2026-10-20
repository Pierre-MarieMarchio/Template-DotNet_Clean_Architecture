namespace AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;

public enum TwoFactorRedemptionStatus
{
    Verified,

    InvalidChallenge,

    InvalidCode,
}
