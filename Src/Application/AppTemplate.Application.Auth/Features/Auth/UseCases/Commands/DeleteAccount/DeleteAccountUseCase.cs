using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Policies;
using AppTemplate.Application.Auth.Features.Auth.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DeleteAccount;

/// <summary>
/// Removes exactly the account row and what ASP.NET Identity owns alongside it — see
/// <see cref="IAccountAdministrationService"/> for why nothing here reaches past that. No
/// <c>CredentialInvalidationPolicy</c> call either: there is no security stamp left to rotate once the row
/// is gone, and the refresh-token grants go with it through the store's own cascading delete.
/// </summary>
public sealed class DeleteAccountUseCase(
    IAccountAdministrationService administration,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<DeleteAccountCommand> validator) : IDeleteAccountUseCase
{
    /// <inheritdoc />
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

        var guard = SelfAdministrationPolicy.EnsureNotSelf(
            callerId.Value.Value,
            request.UserId,
            AuthErrors.CannotDeleteOwnAccount);

        if (guard.IsFailure)
        {
            return guard;
        }

        var outcome = await administration.DeleteAsync(request.UserId, cancellationToken);

        if (outcome.Status is not AccountAdministrationStatus.Applied)
        {
            return Result.Failure(ToError(outcome.Status));
        }

        securityEventLog.Record(SecurityEvent.AccountDeleted(request.UserId));

        return Result.Success();
    }

    private static Error ToError(AccountAdministrationStatus status) => status switch
    {
        AccountAdministrationStatus.NoSuchAccount => AuthErrors.NoSuchAccount,
        _ => AuthErrors.AccountDeletionRejected,
    };
}
