using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Auth.Ports.ReAuthentication;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Domain.Core.Common.Primitives;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DeleteMyAccount;

/// <summary>Deletes the caller's own account, once they prove the session is the holder's own and
/// not merely a live one: <b>the password, if one is set, plus a two-factor code, if the factor is
/// armed</b> — an account with neither falls back to an access token issued less than
/// <see cref="RecentAuthenticationWindow"/> ago, so a stolen session at least has to be a fresh
/// one. The account row and what the user store holds for it are deleted in one committing call;
/// the <see cref="UserAccountDeleted"/> fact follows, published after the deletion has committed, so
/// the features that own data under this id hear it from their own consumers and the sweep, not from
/// this use case reaching past the module boundary.</summary>
public sealed class DeleteMyAccountUseCase(
    IAccountsService accounts,
    IReAuthenticationService reAuthentication,
    ICurrentAccessToken currentAccessToken,
    ISecurityEventLog securityEventLog,
    IDomainEventPublisher domainEvents,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IValidator<DeleteMyAccountCommand> validator) : IDeleteMyAccountUseCase
{
    /// <summary>How recently the access token must have been issued when the account offers no
    /// credential its holder could present: minutes, the order of a token's own lifetime.</summary>
    public static readonly TimeSpan RecentAuthenticationWindow = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(DeleteMyAccountCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId;
        }

        var userId = callerId.Value.Value;

        var profile = await accounts.FindProfileAsync(userId, cancellationToken);

        // A token that outlived its account, answered as GetCurrentUserUseCase answers it.
        if (profile is null)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        // Which evidence the account demands is decided here, from the store, and not by the caller:
        // a request that presents nothing proves nothing, and one that presents a password for an
        // account that has none proves it no better.
        if (profile.HasPassword)
        {
            if (request.CurrentPassword is null)
            {
                return Result.Failure(AuthErrors.MissingCurrentPassword);
            }

            if (!await reAuthentication.VerifyPasswordAsync(userId, request.CurrentPassword, cancellationToken))
            {
                return Result.Failure(AuthErrors.IncorrectCurrentPassword);
            }
        }

        if (profile.TwoFactorEnabled)
        {
            if (request.TwoFactorCode is null)
            {
                return Result.Failure(AuthErrors.MissingTwoFactorCode);
            }

            if (!await reAuthentication.VerifyTwoFactorCodeAsync(userId, request.TwoFactorCode, cancellationToken))
            {
                return Result.Failure(AuthErrors.InvalidTwoFactorConfirmationCode);
            }
        }

        if (!profile.HasPassword && !profile.TwoFactorEnabled)
        {
            // The fallback proof: the token itself, recently issued. A missing claim is a refusal,
            // never a pass.
            if (currentAccessToken.IssuedAt is not { } issuedAt
                || dateTimeProvider.UtcNow - issuedAt > RecentAuthenticationWindow)
            {
                return Result.Failure(AuthErrors.ReAuthenticationRequired);
            }
        }

        var deletion = await accounts.DeleteAsync(userId, cancellationToken);

        // The account left between the profile read and the delete: the same answer as the profile
        // read above, not a 404 — the caller's token no longer names anything either way.
        if (deletion.Status is AccountDeletionStatus.NoSuchAccount)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        if (deletion.Status is not AccountDeletionStatus.Deleted)
        {
            return Result.Failure(AuthErrors.AccountDeletionRejected);
        }

        securityEventLog.Record(SecurityEvent.AccountSelfDeleted(userId));

        // After the deletion has committed, in the same request: the fact names what is already
        // true, and nothing downstream can observe a deletion the store might still have refused.
        await domainEvents.PublishAsync(
            new UserAccountDeleted(UserId.Create(userId), dateTimeProvider.UtcNow),
            cancellationToken);

        return Result.Success();
    }
}
