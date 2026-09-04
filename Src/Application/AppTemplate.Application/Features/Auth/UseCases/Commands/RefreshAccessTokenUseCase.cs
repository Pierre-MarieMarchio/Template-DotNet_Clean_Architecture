using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Dtos;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Application.Features.Auth.Validators;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands;

/// <summary>The presented token is always consumed, success or failure.</summary>
public sealed record RefreshAccessTokenRequest(string RefreshToken);

public interface IRefreshAccessTokenUseCase : IUseCase<RefreshAccessTokenRequest, Result<RefreshAccessTokenResponse>>;

public sealed class RefreshAccessTokenUseCase(
    IUserAccounts accounts,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenGrants refreshTokens,
    IValidator<RefreshAccessTokenRequest> validator) : IRefreshAccessTokenUseCase
{
    public async Task<Result<RefreshAccessTokenResponse>> ExecuteAsync(
        RefreshAccessTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.ValidateAsync(request, cancellationToken);

        if (!validation.IsValid)
        {
            return Result.Failure<RefreshAccessTokenResponse>(validation.ToError());
        }

        // Rotation comes first and is single-use, so a replayed token is refused here rather than
        // after the account has been revalidated.
        var rotation = await refreshTokens.RotateAsync(request.RefreshToken, cancellationToken);

        if (rotation is not { Succeeded: true, UserId: { } userId, Token: { } refreshToken })
        {
            return Result.Failure<RefreshAccessTokenResponse>(AuthErrors.InvalidRefreshToken);
        }

        // A grant issued before the account was locked out or disabled must not keep minting tokens,
        // and the successor has to go with it — otherwise the holder simply refreshes again.
        if (!await accounts.CanSignInAsync(userId, cancellationToken))
        {
            await refreshTokens.RevokeAllForUserAsync(userId, cancellationToken);

            return Result.Failure<RefreshAccessTokenResponse>(AuthErrors.InvalidRefreshToken);
        }

        var accessToken = await accessTokens.IssueAsync(userId, cancellationToken);

        return Result.Success(new RefreshAccessTokenResponse(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt));
    }
}
