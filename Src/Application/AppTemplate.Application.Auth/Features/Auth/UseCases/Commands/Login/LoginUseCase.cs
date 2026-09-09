using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Ports.AccessTokenIssuer;
using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorChallenge;
using AppTemplate.Application.Auth.Features.Auth.Ports.UserAccounts;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Login;

/// <summary>
/// Verifies the credential, then — only once it matched — branches on whether the account has a
/// second factor armed. Nothing is issued and no account fact is disclosed before that first step
/// succeeds.
/// </summary>
public sealed class LoginUseCase(
    IUserAccountsService accounts,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenGrantsService refreshTokens,
    ITwoFactorChallengeService twoFactorChallenge,
    ISecurityEventLog securityEventLog,
    IValidator<LoginCommand> validator) : ILoginUseCase
{
    /// <inheritdoc />
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

        // Every refusal collapses to one error: an unknown address, a wrong password, an unconfirmed
        // address and a lockout are exactly what a probe is trying to tell apart.
        if (credential is not { Status: CredentialCheckStatus.Verified, Account: { } account })
        {
            securityEventLog.Record(SecurityEvent.AuthenticationFailed(credential.Account?.UserId, credential.Status));

            return Result.Failure<LoginOutcome>(AuthErrors.InvalidCredentials);
        }

        // A verified password on a two-factor account is half a login: the challenge issued here is
        // redeemed by VerifyTwoFactorUseCase, which is what mints the tokens. After the credential
        // check, never before, so a guess alone reveals nothing about the account.
        if (account.TwoFactorEnabled)
        {
            var challenge = await twoFactorChallenge.IssueAsync(account.UserId, cancellationToken);

            return Result.Success<LoginOutcome>(new LoginOutcome.TwoFactorRequired(challenge.ChallengeToken));
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
