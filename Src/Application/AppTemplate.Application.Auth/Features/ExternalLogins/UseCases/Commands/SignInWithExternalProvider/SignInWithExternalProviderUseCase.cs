using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.ExternalLogins.Model.Errors;
using AppTemplate.Application.Auth.Features.ExternalLogins.Model.Policies;
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalIdentity;
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalLogins;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;

public sealed class SignInWithExternalProviderUseCase(
    IExternalIdentityVerifier verifier,
    IExternalLoginsService externalLogins,
    IAccountsService accounts,
    ISessionTokensService sessionTokens,
    ITwoFactorService twoFactor,
    ISecurityEventLog securityEventLog,
    ICurrentSession currentSession,
    IValidator<SignInWithExternalProviderCommand> validator) : ISignInWithExternalProviderUseCase
{
    public async Task<Result<SignInWithExternalProviderOutcome>> ExecuteAsync(
        SignInWithExternalProviderCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<SignInWithExternalProviderOutcome>();
        }

        var identity = await verifier.VerifyAsync(request.Provider, request.IdToken, cancellationToken);

        if (identity is null)
        {
            securityEventLog.Record(
                SecurityEvent.AuthenticationFailed(null, CredentialCheckStatus.NoSuchAccount));

            return Refused();
        }

        var linked = await externalLogins.FindByExternalLoginAsync(
            identity.Provider,
            identity.Subject,
            cancellationToken);

        if (linked is not null)
        {
            return await CompleteSignInAsync(linked, accountCreated: false, cancellationToken);
        }

        if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
        {
            securityEventLog.Record(
                SecurityEvent.AuthenticationFailed(null, CredentialCheckStatus.NoSuchAccount));

            return Refused();
        }

        var match = await externalLogins.FindByEmailAsync(identity.Email, cancellationToken);

        return ExternalAccountLinkPolicy.Decide(match) switch
        {
            ExternalAccountLinkDecision.Provision =>
                await ProvisionAsync(identity, cancellationToken),

            ExternalAccountLinkDecision.Link =>
                await LinkAsync(match!.Account, identity, cancellationToken),

            _ => RefuseUnconfirmedLink(match!.Account),
        };
    }

    private static Result<SignInWithExternalProviderOutcome> Refused() =>
        Result.Failure<SignInWithExternalProviderOutcome>(ExternalLoginsErrors.ExternalSignInRefused);

    private async Task<Result<SignInWithExternalProviderOutcome>> ProvisionAsync(
        VerifiedExternalIdentity identity,
        CancellationToken cancellationToken)
    {
        var account = await externalLogins.ProvisionAsync(
            identity.Email!,
            identity.Provider,
            identity.Subject,
            cancellationToken);

        if (account is null)
        {
            return Refused();
        }

        securityEventLog.Record(SecurityEvent.Registered(account.UserId));

        return await CompleteSignInAsync(account, accountCreated: true, cancellationToken);
    }

    private async Task<Result<SignInWithExternalProviderOutcome>> LinkAsync(
        AccountIdentity account,
        VerifiedExternalIdentity identity,
        CancellationToken cancellationToken)
    {
        bool linked = await externalLogins.LinkAsync(
            account.UserId,
            identity.Provider,
            identity.Subject,
            cancellationToken);

        if (!linked)
        {
            return Refused();
        }

        return await CompleteSignInAsync(account, accountCreated: false, cancellationToken);
    }

    private Result<SignInWithExternalProviderOutcome> RefuseUnconfirmedLink(AccountIdentity account)
    {
        securityEventLog.Record(
            SecurityEvent.AuthenticationFailed(account.UserId, CredentialCheckStatus.EmailNotConfirmed));

        return Refused();
    }

    private async Task<Result<SignInWithExternalProviderOutcome>> CompleteSignInAsync(
        AccountIdentity account,
        bool accountCreated,
        CancellationToken cancellationToken)
    {
        if (!await accounts.CanSignInAsync(account.UserId, cancellationToken))
        {
            securityEventLog.Record(
                SecurityEvent.AuthenticationFailed(account.UserId, CredentialCheckStatus.LockedOut));

            return Refused();
        }

        if (account.TwoFactorEnabled)
        {
            var challenge = await twoFactor.IssueChallengeAsync(account.UserId, cancellationToken);

            return Result.Success<SignInWithExternalProviderOutcome>(
                new SignInWithExternalProviderOutcome.TwoFactorRequired(challenge.ChallengeToken));
        }

        securityEventLog.Record(SecurityEvent.LoginSucceeded(account.UserId));

        var refreshToken = await sessionTokens.IssueRefreshTokenAsync(
            account.UserId,
            currentSession.Client,
            cancellationToken);
        var accessToken = await sessionTokens.IssueAccessTokenAsync(
            account.UserId,
            refreshToken.SessionId,
            cancellationToken);

        return Result.Success<SignInWithExternalProviderOutcome>(
            new SignInWithExternalProviderOutcome.Authenticated(
                account.UserId,
                account.Email,
                accessToken.Value,
                accessToken.ExpiresAt,
                refreshToken.Value,
                refreshToken.ExpiresAt,
                accountCreated));
    }
}
