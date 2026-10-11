using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Policies;
using AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Auth.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ChangePassword;

/// <summary>
/// Replaces the password, then invalidates every credential issued under the old one through
/// <c>CredentialInvalidationPolicy</c> — in that order, so nothing is revoked for a change the
/// store refused. The session the change is made from survives it: whoever just proved the current
/// password is the one session that need not prove it again. A caller whose token names no session
/// loses every session, its own included.
/// </summary>
public sealed class ChangePasswordUseCase(
    IAccountsService accounts,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    ICurrentSession currentSession,
    IValidator<ChangePasswordCommand> validator) : IChangePasswordUseCase
{
    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(ChangePasswordCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId;
        }

        var change = await accounts.ChangePasswordAsync(
            userId.Value.Value,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);

        // A token that outlived its account, answered as GetCurrentUserUseCase answers it.
        if (change.Status is PasswordChangeStatus.NoSuchAccount)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        if (change.Status is PasswordChangeStatus.IncorrectCurrentPassword)
        {
            return Result.Failure(AuthErrors.IncorrectCurrentPassword);
        }

        if (change.Status is PasswordChangeStatus.Rejected)
        {
            return Result.Failure(
                AuthErrors.PasswordChangeRejected(
                    change.RejectionMessage ?? "The submitted password does not meet the required policy."));
        }

        await CredentialInvalidationPolicy.InvalidateOtherSessionsAsync(
            sessionTokens,
            securityEventLog,
            userId.Value.Value,
            currentSession.SessionId,
            cancellationToken);

        return Result.Success();
    }
}
