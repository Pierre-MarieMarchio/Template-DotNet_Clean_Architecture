using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Model.Errors;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RefreshAccessToken;

public sealed record RefreshAccessTokenCommand(string RefreshToken) : ICommand<Result<RefreshAccessTokenOutcome>>;

public sealed class RefreshAccessTokenCommandHandler(
    IAccountsService accounts,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentSession currentSession) : ICommandHandler<RefreshAccessTokenCommand, Result<RefreshAccessTokenOutcome>>
{
    public async ValueTask<Result<RefreshAccessTokenOutcome>> Handle(
        RefreshAccessTokenCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rotation = await sessionTokens.RotateRefreshTokenAsync(
            request.RefreshToken,
            currentSession.Client,
            cancellationToken);

        if (rotation is not { Succeeded: true, UserId: { } userId, Token: { } refreshToken })
        {
            return Result.Failure<RefreshAccessTokenOutcome>(SessionsErrors.InvalidRefreshToken);
        }

        if (!await accounts.CanSignInAsync(userId, cancellationToken))
        {
            await sessionTokens.RevokeAllRefreshTokensAsync(userId, cancellationToken: cancellationToken);
            securityEventLog.Record(SecurityEvent.RefreshTokenRevoked(userId));

            return Result.Failure<RefreshAccessTokenOutcome>(SessionsErrors.InvalidRefreshToken);
        }

        var accessToken = await sessionTokens.IssueAccessTokenAsync(userId, refreshToken.SessionId, cancellationToken);

        return Result.Success(new RefreshAccessTokenOutcome(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt));
    }
}
