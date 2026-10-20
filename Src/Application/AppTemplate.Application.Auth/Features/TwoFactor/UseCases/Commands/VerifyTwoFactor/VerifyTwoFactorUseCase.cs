using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Auth.Features.TwoFactor.Model.Errors;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.VerifyTwoFactor;

public sealed class VerifyTwoFactorUseCase(
    ITwoFactorService twoFactor,
    IAccountsService accounts,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentSession currentSession,
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

        var redemption = await twoFactor.RedeemChallengeAsync(request.ChallengeToken, request.Code, cancellationToken);

        if (redemption is { Status: TwoFactorRedemptionStatus.InvalidCode, Account: { } failedAccount })
        {
            securityEventLog.Record(SecurityEvent.TwoFactorChallengeFailed(failedAccount.UserId));
        }

        if (redemption is not { Status: TwoFactorRedemptionStatus.Verified, Account: { } account })
        {
            return Result.Failure<LoginOutcome>(TwoFactorErrors.InvalidTwoFactorChallenge);
        }

        if (!await accounts.CanSignInAsync(account.UserId, cancellationToken))
        {
            return Result.Failure<LoginOutcome>(TwoFactorErrors.InvalidTwoFactorChallenge);
        }

        securityEventLog.Record(SecurityEvent.LoginSucceeded(account.UserId));

        if (redemption.UsedRecoveryCode)
        {
            securityEventLog.Record(SecurityEvent.RecoveryCodeRedeemed(account.UserId));
        }

        var refreshToken = await sessionTokens.IssueRefreshTokenAsync(
            account.UserId,
            currentSession.Client,
            cancellationToken);
        var accessToken = await sessionTokens.IssueAccessTokenAsync(
            account.UserId,
            refreshToken.SessionId,
            cancellationToken);

        return Result.Success<LoginOutcome>(new LoginOutcome.Authenticated(
            account.UserId,
            account.Email,
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt));
    }
}
