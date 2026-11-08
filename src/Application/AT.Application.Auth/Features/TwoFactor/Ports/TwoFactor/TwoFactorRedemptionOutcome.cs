using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

namespace AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;

public sealed record TwoFactorRedemptionOutcome(
    TwoFactorRedemptionStatus Status,
    AccountIdentity? Account = null,
    bool UsedRecoveryCode = false)
{
    public static TwoFactorRedemptionOutcome InvalidChallenge { get; } =
        new(TwoFactorRedemptionStatus.InvalidChallenge);

    public static TwoFactorRedemptionOutcome InvalidCode(AccountIdentity account) =>
        new(TwoFactorRedemptionStatus.InvalidCode, account);

    public static TwoFactorRedemptionOutcome Verified(AccountIdentity account, bool usedRecoveryCode) =>
        new(TwoFactorRedemptionStatus.Verified, account, usedRecoveryCode);
}
