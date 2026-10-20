namespace AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;

public interface ITwoFactorService
{
    Task<TwoFactorSetupOutcome> BeginSetupAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<TwoFactorConfirmationOutcome> ConfirmSetupAsync(
        Guid userId,
        string currentPassword,
        string code,
        CancellationToken cancellationToken = default);

    Task<TwoFactorDisableStatus> DisableAsync(
        Guid userId,
        string currentPassword,
        CancellationToken cancellationToken = default);

    Task<IssuedTwoFactorChallenge> IssueChallengeAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<TwoFactorRedemptionOutcome> RedeemChallengeAsync(
        string challengeToken,
        string code,
        CancellationToken cancellationToken = default);
}
