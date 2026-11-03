using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Passkeys.Model.Errors;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeySignIn;

public sealed record CompletePasskeySignInCommand(string Credential, string AuthenticationState) : ICommand<Result<LoginOutcome>>;

public sealed class CompletePasskeySignInCommandHandler(
    IPasskeyService passkeys,
    IAccountsService accounts,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentSession currentSession) : ICommandHandler<CompletePasskeySignInCommand, Result<LoginOutcome>>
{
    public async ValueTask<Result<LoginOutcome>> Handle(
        CompletePasskeySignInCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var assertion = await passkeys.CompleteSignInAsync(
            request.Credential,
            request.AuthenticationState,
            cancellationToken);

        if (assertion.Status is not PasskeySignInStatus.Authenticated || assertion.Account is not { } account)
        {
            await securityEventLog.RecordAsync(SecurityEvent.PasskeySignInFailed(null));

            return Result.Failure<LoginOutcome>(PasskeysErrors.InvalidPasskeySignIn);
        }

        if (!await accounts.CanSignInAsync(account.UserId, cancellationToken))
        {
            await securityEventLog.RecordAsync(SecurityEvent.PasskeySignInFailed(account.UserId));

            return Result.Failure<LoginOutcome>(PasskeysErrors.InvalidPasskeySignIn);
        }

        await securityEventLog.RecordAsync(SecurityEvent.PasskeySignInSucceeded(account.UserId));

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
