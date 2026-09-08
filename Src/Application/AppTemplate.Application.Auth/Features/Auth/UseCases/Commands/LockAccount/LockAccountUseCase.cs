using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Policies;
using AppTemplate.Application.Auth.Features.Auth.Ports.AccountLockouts;
using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.LockAccount;

/// <summary>
/// Suspends sign-in indefinitely and then cuts the account's live sessions: a lockout that left
/// refresh tokens alone would suspend nothing the holder is already using.
/// </summary>
public sealed class LockAccountUseCase(
    IAccountLockoutsService lockouts,
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<LockAccountCommand> validator) : ILockAccountUseCase
{
    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(LockAccountCommand request, CancellationToken cancellationToken = default)
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

        var guard = SelfAdministrationPolicy.EnsureNotSelf(
            callerId.Value.Value,
            request.UserId,
            AuthErrors.CannotLockOwnAccount);

        if (guard.IsFailure)
        {
            return guard;
        }

        var outcome = await lockouts.LockAsync(request.UserId, cancellationToken);

        if (outcome is not LockoutChangeStatus.Applied)
        {
            return Result.Failure(ToError(outcome));
        }

        await CredentialInvalidationPolicy.InvalidateAsync(refreshTokens, securityEventLog, request.UserId, cancellationToken);
        securityEventLog.Record(SecurityEvent.AccountLockedByAdministrator(request.UserId));

        return Result.Success();
    }

    private static Error ToError(LockoutChangeStatus outcome) => outcome switch
    {
        LockoutChangeStatus.NoSuchAccount => AuthErrors.NoSuchAccount,
        _ => AuthErrors.AccountLockoutRejected,
    };
}
