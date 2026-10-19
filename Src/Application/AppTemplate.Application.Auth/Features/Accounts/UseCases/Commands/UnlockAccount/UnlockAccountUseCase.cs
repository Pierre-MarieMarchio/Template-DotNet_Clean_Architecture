using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.UnlockAccount;

/// <summary>
/// No caller-identity check and no <c>CredentialInvalidationPolicy</c> call, unlike
/// <see cref="LockAccount.LockAccountUseCase"/>: lifting a lockout grants access back rather than
/// taking it away, so there is no session of the caller's own it could end, and no credential of the
/// target's it needs to invalidate.
/// </summary>
public sealed class UnlockAccountUseCase(
    IAccountAdministrationService administration,
    ISecurityEventLog securityEventLog,
    IValidator<UnlockAccountCommand> validator) : IUnlockAccountUseCase
{
    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(UnlockAccountCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var outcome = await administration.UnlockAsync(request.UserId, cancellationToken);

        if (outcome.Status is not AccountAdministrationStatus.Applied)
        {
            return Result.Failure(ToError(outcome.Status));
        }

        securityEventLog.Record(SecurityEvent.AccountUnlockedByAdministrator(request.UserId));

        return Result.Success();
    }

    private static Error ToError(AccountAdministrationStatus status) => status switch
    {
        AccountAdministrationStatus.NoSuchAccount => AccountsErrors.NoSuchAccount,
        _ => AccountsErrors.AccountLockoutRejected,
    };
}
