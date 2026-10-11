using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Auth.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Auth.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Login;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.CompletePasskeySignIn;

/// <summary>Finishes a passkey sign-in: the assertion is verified against the ceremony's
/// challenge, the account is checked to still be allowed in, and only then is a session started.
/// A verified passkey stands in for the whole of a sign-in, an armed second factor included — the
/// ceremony required user verification, so no challenge is issued here and
/// <see cref="LoginOutcome.TwoFactorRequired"/> is never this use case's answer; the armed factor
/// still governs the ways in that prove less. Every refusal collapses to
/// <see cref="AuthErrors.InvalidPasskeySignIn"/>.</summary>
public sealed class CompletePasskeySignInUseCase(
    IPasskeyService passkeys,
    IAccountsService accounts,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentSession currentSession,
    IValidator<CompletePasskeySignInCommand> validator) : ICompletePasskeySignInUseCase
{
    /// <inheritdoc />
    public async Task<Result<LoginOutcome>> ExecuteAsync(
        CompletePasskeySignInCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<LoginOutcome>();
        }

        var assertion = await passkeys.CompleteSignInAsync(
            request.Credential,
            request.AuthenticationState,
            cancellationToken);

        if (assertion.Status is not PasskeySignInStatus.Authenticated || assertion.Account is not { } account)
        {
            securityEventLog.Record(SecurityEvent.PasskeySignInFailed(null));

            return Result.Failure<LoginOutcome>(AuthErrors.InvalidPasskeySignIn);
        }

        // The same guard every other way in applies once the account is identified: a locked-out
        // account answers its passkey the way it answers its password.
        if (!await accounts.CanSignInAsync(account.UserId, cancellationToken))
        {
            securityEventLog.Record(SecurityEvent.PasskeySignInFailed(account.UserId));

            return Result.Failure<LoginOutcome>(AuthErrors.InvalidPasskeySignIn);
        }

        securityEventLog.Record(SecurityEvent.PasskeySignInSucceeded(account.UserId));

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
