using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Policies;
using AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Auth.Ports.ExternalIdentity;
using AppTemplate.Application.Auth.Features.Auth.Ports.ExternalLogins;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.SignInWithExternalProvider;

/// <summary>
/// Turns an <c>id_token</c> the client obtained from an identity provider into this API's own token
/// pair, creating or linking a local account on the way.
/// <para>
/// The provider's redirect flow runs on the client and this endpoint takes only its result, so
/// nothing here holds a cookie or issues a redirect and the token model is untouched: a browser app,
/// a phone and a desktop client all use the same two calls.
/// </para>
/// </summary>
public sealed class SignInWithExternalProviderUseCase(
    IExternalIdentityVerifier verifier,
    IExternalLoginsService externalLogins,
    IAccountsService accounts,
    ISessionTokensService sessionTokens,
    ITwoFactorService twoFactor,
    ISecurityEventLog securityEventLog,
    IValidator<SignInWithExternalProviderCommand> validator) : ISignInWithExternalProviderUseCase
{
    /// <inheritdoc />
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

        // Every refusal — a forged token, an expired one, an unconfigured provider — answers the
        // same way, so this cannot be used to learn which providers the installation accepts.
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

        // Case 1 — the provider identity is already on file, and the address is not consulted:
        // Apple returns one only on the first authorisation.
        if (linked is not null)
        {
            return await CompleteSignInAsync(linked, accountCreated: false, cancellationToken);
        }

        // First link, and the only step that uses the address. An address the provider did not state
        // it had checked is one the caller chose, so it resolves nothing.
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

            // Case 4 — the address belongs to an unconfirmed account. Refusing stops whoever
            // registered it from being handed the account its real owner is creating.
            _ => RefuseUnconfirmedLink(match!.Account),
        };
    }

    private static Result<SignInWithExternalProviderOutcome> Refused() =>
        Result.Failure<SignInWithExternalProviderOutcome>(AuthErrors.ExternalSignInRefused);

    /// <summary>Case 3 — nobody holds the address, so the account is created and linked in one step.</summary>
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

    /// <summary>
    /// Case 2 — a confirmed local account holds the address. Two independent proofs of the same
    /// address, one from the account and one from the provider, is the best evidence either side has
    /// that this is the same person.
    /// </summary>
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

    /// <summary>
    /// The tail every path shares: the same lockout check and the same second factor a password
    /// sign-in goes through. A provider proves identity, not that the account is still allowed to
    /// sign in, and it does not stand in for a factor the owner armed themselves.
    /// </summary>
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

        var accessToken = await sessionTokens.IssueAccessTokenAsync(account.UserId, cancellationToken);
        var refreshToken = await sessionTokens.IssueRefreshTokenAsync(account.UserId, cancellationToken);

        return Result.Success<SignInWithExternalProviderOutcome>(
            new SignInWithExternalProviderOutcome.Authenticated(
                account.UserId,
                account.UserName,
                account.Email,
                accessToken.Value,
                accessToken.ExpiresAt,
                refreshToken.Value,
                refreshToken.ExpiresAt,
                accountCreated));
    }
}
