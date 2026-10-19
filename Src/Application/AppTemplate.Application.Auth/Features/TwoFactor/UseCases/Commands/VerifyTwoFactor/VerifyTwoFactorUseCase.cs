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

/// <summary>
/// Finishes a two-step login: redeems the challenge, re-asks whether the account may still sign
/// in, and only then issues the pair. A wrong code is recorded against the account and answered
/// exactly as a spent challenge is.
/// </summary>
public sealed class VerifyTwoFactorUseCase(
    ITwoFactorService twoFactor,
    IAccountsService accounts,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentSession currentSession,
    IValidator<VerifyTwoFactorCommand> validator) : IVerifyTwoFactorUseCase
{
    /// <inheritdoc />
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

        // Both refusals answer identically — see TwoFactorErrors.InvalidTwoFactorChallenge — so nothing
        // beyond the audit trail above distinguishes a spent challenge from a wrong code.
        if (redemption is not { Status: TwoFactorRedemptionStatus.Verified, Account: { } account })
        {
            return Result.Failure<LoginOutcome>(TwoFactorErrors.InvalidTwoFactorChallenge);
        }

        // A challenge issued before the account was locked out, disabled or deleted must not still
        // mint tokens once redeemed — the same guard RefreshAccessTokenUseCase applies to a presented
        // refresh token.
        if (!await accounts.CanSignInAsync(account.UserId, cancellationToken))
        {
            return Result.Failure<LoginOutcome>(TwoFactorErrors.InvalidTwoFactorChallenge);
        }

        securityEventLog.Record(SecurityEvent.LoginSucceeded(account.UserId));

        if (redemption.UsedRecoveryCode)
        {
            securityEventLog.Record(SecurityEvent.RecoveryCodeRedeemed(account.UserId));
        }

        // The refresh token first: it starts the session the access token names.
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
            account.UserName,
            account.Email,
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt));
    }
}
