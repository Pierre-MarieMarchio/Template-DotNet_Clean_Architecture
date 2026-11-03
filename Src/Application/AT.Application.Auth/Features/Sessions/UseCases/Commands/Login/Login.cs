using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Model.Errors;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;

public sealed record LoginCommand(string Email, string Password) : ICommand<Result<LoginOutcome>>;

public sealed class LoginCommandHandler(
    IAccountsService accounts,
    ISessionTokensService sessionTokens,
    ITwoFactorService twoFactor,
    ISecurityEventLog securityEventLog,
    ICurrentSession currentSession) : ICommandHandler<LoginCommand, Result<LoginOutcome>>
{
    public async ValueTask<Result<LoginOutcome>> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var credential = await accounts.VerifyCredentialAsync(
            request.Email,
            request.Password,
            cancellationToken);

        if (credential is not { Status: CredentialCheckStatus.Verified, Account: { } account })
        {
            if (credential is { Status: CredentialCheckStatus.LockedOut, Account: { } lockedOut })
            {
                await securityEventLog.RecordAsync(SecurityEvent.AccountLockedOut(lockedOut.UserId));
            }

            await securityEventLog.RecordAsync(SecurityEvent.AuthenticationFailed(credential.Account?.UserId, credential.Status));

            return Result.Failure<LoginOutcome>(SessionsErrors.InvalidCredentials);
        }

        if (account.TwoFactorEnabled)
        {
            var challenge = await twoFactor.IssueChallengeAsync(account.UserId, cancellationToken);

            return Result.Success<LoginOutcome>(new LoginOutcome.TwoFactorRequired(challenge.ChallengeToken));
        }

        await securityEventLog.RecordAsync(SecurityEvent.LoginSucceeded(account.UserId));

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
