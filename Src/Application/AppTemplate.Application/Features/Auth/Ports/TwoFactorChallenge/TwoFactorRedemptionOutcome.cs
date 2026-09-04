using AppTemplate.Application.Features.Auth.Ports.UserAccounts;

namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorChallenge;

/// <param name="Account">
/// Present for <see cref="TwoFactorRedemptionStatus.Verified"/> and for
/// <see cref="TwoFactorRedemptionStatus.InvalidCode"/> alike — a live challenge was found either
/// way, so there is an account to attribute a failed attempt to in the audit trail, and to issue
/// tokens for once one succeeds. Absent for <see cref="TwoFactorRedemptionStatus.InvalidChallenge"/>:
/// nothing live was ever found.
/// </param>
/// <param name="UsedRecoveryCode">
/// Set only for <see cref="TwoFactorRedemptionStatus.Verified"/>, so the use case can record that a
/// one-time code — not the authenticator app — completed this sign-in.
/// </param>
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
