using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Dtos;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Application.Features.Auth.Validators;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands;

public sealed record LoginRequest(string Email, string Password);

public interface ILoginUseCase : IUseCase<LoginRequest, Result<LoginResponse>>;

public sealed class LoginUseCase(
    IUserAccounts accounts,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenGrants refreshTokens,
    IValidator<LoginRequest> validator) : ILoginUseCase
{
    public async Task<Result<LoginResponse>> ExecuteAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.ValidateAsync(request, cancellationToken);

        if (!validation.IsValid)
        {
            return Result.Failure<LoginResponse>(validation.ToError());
        }

        var credential = await accounts.VerifyCredentialAsync(
            request.Email,
            request.Password,
            cancellationToken);

        // Every refusal collapses to one error, whatever the reason: an unknown address, a wrong
        // password, an unconfirmed address and a locked-out account are exactly what a probe is
        // trying to tell apart. Branching on the outcome here is what would let it.
        if (credential is not { Outcome: CredentialCheckOutcome.Verified, Account: { } account })
        {
            return Result.Failure<LoginResponse>(AuthErrors.InvalidCredentials);
        }

        var accessToken = await accessTokens.IssueAsync(account.UserId, cancellationToken);
        var refreshToken = await refreshTokens.IssueAsync(account.UserId, cancellationToken);

        return Result.Success(new LoginResponse(
            account.UserId,
            account.UserName,
            account.Email,
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt));
    }
}
