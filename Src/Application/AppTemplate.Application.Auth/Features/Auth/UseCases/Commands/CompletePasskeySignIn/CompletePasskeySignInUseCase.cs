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

/// <summary>
/// Finishes a passkey sign-in: the credential's assertion is verified against the ceremony's
/// challenge, the account it names is checked to still be allowed in, and only then is a session
/// started — one sign-in, one session, exactly as a password's is.
/// <para>
/// A verified passkey stands in for the whole of a sign-in, an armed second factor included: the
/// ceremony's user-verification requirement is <c>required</c> — see
/// <c>AppTemplate.Infrastructure.Auth</c>'s passkey options — so possession of the credential and
/// the authenticator's own user check happened together, which is the two factors a password and a
/// TOTP code provide. No challenge is issued here, and <see cref="LoginOutcome.TwoFactorRequired"/>
/// is never this use case's answer. The account's armed factor still governs the ways in that
/// prove less: a password sign-in demands it exactly as before.
/// </para>
/// <para>
/// Every refusal collapses to one error — a ceremony that did not verify, a credential the store
/// no longer knows, an account that may no longer sign in — for the reason
/// <see cref="AuthErrors.InvalidPasskeySignIn"/> gives.
/// </para>
/// </summary>
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
