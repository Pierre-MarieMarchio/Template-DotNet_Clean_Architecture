using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.Ports.AccountDeletion;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.DeleteAccount;

/// <summary>
/// Removes exactly the account row and what ASP.NET Identity owns alongside it — see
/// <see cref="IAccountDeletion"/> for why nothing here reaches past that. No
/// <c>CredentialInvalidation</c> call either: there is no security stamp left to rotate once the row
/// is gone, and the refresh-token grants go with it through the store's own cascading delete.
/// </summary>
public sealed class DeleteAccountUseCase(
    IAccountDeletion accountDeletion,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<DeleteAccountCommand> validator) : IDeleteAccountUseCase
{
    public async Task<Result> ExecuteAsync(DeleteAccountCommand request, CancellationToken cancellationToken = default)
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
            AuthErrors.CannotDeleteOwnAccount);

        if (guard.IsFailure)
        {
            return guard;
        }

        var outcome = await accountDeletion.DeleteAsync(request.UserId, cancellationToken);

        if (outcome is not AccountDeletionStatus.Deleted)
        {
            return Result.Failure(ToError(outcome));
        }

        securityEventLog.Record(SecurityEvent.AccountDeleted(request.UserId));

        return Result.Success();
    }

    private static Error ToError(AccountDeletionStatus outcome) => outcome switch
    {
        AccountDeletionStatus.NoSuchAccount => AuthErrors.NoSuchAccount,
        _ => AuthErrors.AccountDeletionRejected,
    };
}
