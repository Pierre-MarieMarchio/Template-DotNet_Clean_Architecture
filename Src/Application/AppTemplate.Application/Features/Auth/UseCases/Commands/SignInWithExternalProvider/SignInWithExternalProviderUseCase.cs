using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.Ports.AccessTokenIssuer;
using AppTemplate.Application.Features.Auth.Ports.ExternalIdentity;
using AppTemplate.Application.Features.Auth.Ports.ExternalLogins;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorChallenge;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.SignInWithExternalProvider;

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
    IUserAccountsService accounts,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenGrantsService refreshTokens,
    ITwoFactorChallengeService twoFactorChallenge,
    ISecurityEventLog securityEventLog,
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

        var verification = await verifier.VerifyAsync(request.Provider, request.IdToken, cancellationToken);

        // Nothing about the token is believed until the signature, issuer, audience and validity
        // window all hold. Every refusal — a forged token, an expired one, a provider nobody
        // configured — answers the same way, so the endpoint cannot be used to find out which
        // providers this installation accepts.
        if (verification.Status is not ExternalIdentityStatus.Verified || verification.Identity is null)
        {
            securityEventLog.Record(
                SecurityEvent.AuthenticationFailed(null, CredentialCheckStatus.NoSuchAccount));

            return Refused();
        }

        var identity = verification.Identity;

        var linked = await externalLogins.FindByExternalLoginAsync(
            identity.Provider,
            identity.Subject,
            cancellationToken);

        // Case 1 — the provider identity is already on file. The address is not consulted at all,
        // which is the point: Apple returns one only on the first authorisation, so a resolution by
        // address would work in development and fail on every user's second sign-in.
        if (linked is not null)
        {
            return await CompleteSignInAsync(linked, accountCreated: false, cancellationToken);
        }

        // First link, and the only step at which the address is used for anything. An address the
        // provider did not state it had checked is an address the caller chose, so it resolves
        // nothing — and a token with no address at all cannot open a first link either.
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

            // Case 4 — the address belongs to an account nobody ever confirmed. Refusing is what
            // stops whoever registered that address from being handed the account the real owner
            // believes they are creating through their provider.
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
        var provision = await externalLogins.ProvisionAsync(
            identity.Email!,
            identity.Provider,
            identity.Subject,
            cancellationToken);

        if (provision.Status is not ExternalAccountProvisionStatus.Provisioned || provision.Account is null)
        {
            return Refused();
        }

        securityEventLog.Record(SecurityEvent.Registered(provision.Account.UserId));

        return await CompleteSignInAsync(provision.Account, accountCreated: true, cancellationToken);
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
        var link = await externalLogins.LinkAsync(
            account.UserId,
            identity.Provider,
            identity.Subject,
            cancellationToken);

        if (link is not ExternalLoginLinkStatus.Linked)
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
            var challenge = await twoFactorChallenge.IssueAsync(account.UserId, cancellationToken);

            return Result.Success<SignInWithExternalProviderOutcome>(
                new SignInWithExternalProviderOutcome.TwoFactorRequired(challenge.ChallengeToken));
        }

        securityEventLog.Record(SecurityEvent.LoginSucceeded(account.UserId));

        var accessToken = await accessTokens.IssueAsync(account.UserId, cancellationToken);
        var refreshToken = await refreshTokens.IssueAsync(account.UserId, cancellationToken);

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
