using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports.AccessTokenIssuer;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorChallenge;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Login;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.VerifyTwoFactor;

public sealed class VerifyTwoFactorUseCase(
    ITwoFactorChallengeService challenges,
    IUserAccountsService accounts,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    IValidator<VerifyTwoFactorCommand> validator) : IVerifyTwoFactorUseCase
{
    public async Task<Result<LoginOutcome>> ExecuteAsync(
        VerifyTwoFactorCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<LoginOutcome>();
        }

        var redemption = await challenges.RedeemAsync(request.ChallengeToken, request.Code, cancellationToken);

        if (redemption is { Status: TwoFactorRedemptionStatus.InvalidCode, Account: { } failedAccount })
        {
            securityEventLog.Record(SecurityEvent.TwoFactorChallengeFailed(failedAccount.UserId));
        }

        // Both refusals answer identically — see AuthErrors.InvalidTwoFactorChallenge — so nothing
        // beyond the audit trail above distinguishes a spent challenge from a wrong code.
        if (redemption is not { Status: TwoFactorRedemptionStatus.Verified, Account: { } account })
        {
            return Result.Failure<LoginOutcome>(AuthErrors.InvalidTwoFactorChallenge);
        }

        // A challenge issued before the account was locked out, disabled or deleted must not still
        // mint tokens once redeemed — the same guard RefreshAccessTokenUseCase applies to a presented
        // refresh token.
        if (!await accounts.CanSignInAsync(account.UserId, cancellationToken))
        {
            return Result.Failure<LoginOutcome>(AuthErrors.InvalidTwoFactorChallenge);
        }

        securityEventLog.Record(SecurityEvent.LoginSucceeded(account.UserId));

        if (redemption.UsedRecoveryCode)
        {
            securityEventLog.Record(SecurityEvent.RecoveryCodeRedeemed(account.UserId));
        }

        var accessToken = await accessTokens.IssueAsync(account.UserId, cancellationToken);
        var refreshToken = await refreshTokens.IssueAsync(account.UserId, cancellationToken);

        return Result.Success<LoginOutcome>(new LoginOutcome.Authenticated(
            account.UserId,
            account.UserName,
            account.Email,
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt));
    }
}
