using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports.AccessTokenIssuer;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken;

public sealed class RefreshAccessTokenUseCase(
    IUserAccountsService accounts,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    IValidator<RefreshAccessTokenCommand> validator) : IRefreshAccessTokenUseCase
{
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
        var rotation = await refreshTokens.RotateAsync(request.RefreshToken, cancellationToken);

        if (rotation is not { Succeeded: true, UserId: { } userId, Token: { } refreshToken })
        {
            return Result.Failure<RefreshAccessTokenOutcome>(AuthErrors.InvalidRefreshToken);
        }

        // A grant issued before the account was locked out or disabled must not keep minting tokens,
        // and the successor has to go with it — otherwise the holder simply refreshes again.
        if (!await accounts.CanSignInAsync(userId, cancellationToken))
        {
            await refreshTokens.RevokeAllForUserAsync(userId, cancellationToken);
            securityEventLog.Record(SecurityEvent.RefreshTokenRevoked(userId));

            return Result.Failure<RefreshAccessTokenOutcome>(AuthErrors.InvalidRefreshToken);
        }

        var accessToken = await accessTokens.IssueAsync(userId, cancellationToken);

        return Result.Success(new RefreshAccessTokenOutcome(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt));
    }
}
