using AppTemplate.Application.Features.Auth.Ports.UserAccounts;

namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorChallenge;

/// <param name="Account">
/// Present for <see cref="TwoFactorRedemptionOutcome.Verified"/> and for
/// <see cref="TwoFactorRedemptionOutcome.InvalidCode"/> alike — a live challenge was found either
/// way, so there is an account to attribute a failed attempt to in the audit trail, and to issue
/// tokens for once one succeeds. Absent for <see cref="TwoFactorRedemptionOutcome.InvalidChallenge"/>:
/// nothing live was ever found.
/// </param>
/// <param name="UsedRecoveryCode">
/// Set only for <see cref="TwoFactorRedemptionOutcome.Verified"/>, so the use case can record that a
/// one-time code — not the authenticator app — completed this sign-in.
/// </param>
public sealed record TwoFactorRedemption(
    TwoFactorRedemptionOutcome Outcome,
    AccountIdentity? Account = null,
    bool UsedRecoveryCode = false)
{
    public static TwoFactorRedemption InvalidChallenge { get; } =
        new(TwoFactorRedemptionOutcome.InvalidChallenge);

    public static TwoFactorRedemption InvalidCode(AccountIdentity account) =>
        new(TwoFactorRedemptionOutcome.InvalidCode, account);

    public static TwoFactorRedemption Verified(AccountIdentity account, bool usedRecoveryCode) =>
        new(TwoFactorRedemptionOutcome.Verified, account, usedRecoveryCode);
}
