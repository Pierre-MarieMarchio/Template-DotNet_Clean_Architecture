using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.Ports.AccountLockouts;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.LockAccount;

public sealed class LockAccountUseCase(
    IAccountLockouts lockouts,
    IRefreshTokenGrants refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<LockAccountCommand> validator) : ILockAccountUseCase
{
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

        var guard = SelfAdministrationGuard.EnsureNotSelf(
            callerId.Value,
            request.UserId,
            AuthErrors.CannotLockOwnAccount);

        if (guard.IsFailure)
        {
            return guard;
        }

        var outcome = await lockouts.LockAsync(request.UserId, cancellationToken);

        if (outcome is not LockoutChangeOutcome.Applied)
        {
            return Result.Failure(ToError(outcome));
        }

        // The security stamp already rotated inside LockAsync.
        await CredentialInvalidation.InvalidateAsync(refreshTokens, securityEventLog, request.UserId, cancellationToken);
        securityEventLog.Record(SecurityEvent.AccountLockedByAdministrator(request.UserId));

        return Result.Success();
    }

    private static Error ToError(LockoutChangeOutcome outcome) => outcome switch
    {
        LockoutChangeOutcome.NoSuchAccount => AuthErrors.NoSuchAccount,
        _ => AuthErrors.AccountLockoutRejected,
    };
}
