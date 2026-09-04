using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Dtos;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands;

public sealed record LoginCommand(string Email, string Password);

public interface ILoginUseCase : IUseCase<LoginCommand, Result<LoginOutcome>>;

public sealed class LoginUseCase(
    IUserAccounts accounts,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenGrants refreshTokens,
    ISecurityEventLog securityEventLog,
    IValidator<LoginCommand> validator) : ILoginUseCase
{
    public async Task<Result<LoginOutcome>> ExecuteAsync(
        LoginCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<LoginOutcome>();
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
            securityEventLog.Record(SecurityEvent.AuthenticationFailed(credential.Account?.UserId, credential.Outcome));

            return Result.Failure<LoginOutcome>(AuthErrors.InvalidCredentials);
        }

        securityEventLog.Record(SecurityEvent.LoginSucceeded(account.UserId));

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
