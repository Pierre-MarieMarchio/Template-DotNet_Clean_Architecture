using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports.AccountLockouts;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.UnlockAccount;

/// <summary>
/// No caller-identity check and no <c>CredentialInvalidation</c> call, unlike
/// <see cref="LockAccount.LockAccountUseCase"/>: lifting a lockout grants access back rather than
/// taking it away, so there is no session of the caller's own it could end, and no credential of the
/// target's it needs to invalidate.
/// </summary>
public sealed class UnlockAccountUseCase(
    IAccountLockouts lockouts,
    ISecurityEventLog securityEventLog,
    IValidator<UnlockAccountCommand> validator) : IUnlockAccountUseCase
{
    public async Task<Result> ExecuteAsync(UnlockAccountCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var outcome = await lockouts.UnlockAsync(request.UserId, cancellationToken);

        if (outcome is not LockoutChangeOutcome.Applied)
        {
            return Result.Failure(ToError(outcome));
        }

        securityEventLog.Record(SecurityEvent.AccountUnlockedByAdministrator(request.UserId));

        return Result.Success();
    }

    private static Error ToError(LockoutChangeOutcome outcome) => outcome switch
    {
        LockoutChangeOutcome.NoSuchAccount => AuthErrors.NoSuchAccount,
        _ => AuthErrors.AccountLockoutRejected,
    };
}
