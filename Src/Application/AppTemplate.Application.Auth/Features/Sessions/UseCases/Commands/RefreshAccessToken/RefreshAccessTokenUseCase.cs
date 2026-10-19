using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Model.Errors;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RefreshAccessToken;

/// <summary>
/// Rotates the grant first and re-asks whether the account may still sign in second, so a replay
/// is refused before anything is revalidated and a suspended account cannot refresh its way
/// onwards.
/// </summary>
public sealed class RefreshAccessTokenUseCase(
    IAccountsService accounts,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentSession currentSession,
    IValidator<RefreshAccessTokenCommand> validator) : IRefreshAccessTokenUseCase
{
    /// <inheritdoc />
    public async Task<Result<RefreshAccessTokenOutcome>> ExecuteAsync(
        RefreshAccessTokenCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<RefreshAccessTokenOutcome>();
        }

        // Rotation comes first and is single-use, so a replayed token is refused here rather than
        // after the account has been revalidated.
        var rotation = await sessionTokens.RotateRefreshTokenAsync(
            request.RefreshToken,
            currentSession.Client,
            cancellationToken);

        if (rotation is not { Succeeded: true, UserId: { } userId, Token: { } refreshToken })
        {
            return Result.Failure<RefreshAccessTokenOutcome>(SessionsErrors.InvalidRefreshToken);
        }

        // A grant issued before the account was locked out or disabled must not keep minting tokens,
        // and the successor has to go with it — otherwise the holder simply refreshes again.
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
