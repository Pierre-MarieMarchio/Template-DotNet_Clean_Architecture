using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand<Result>;

public sealed class ChangePasswordCommandHandler(
    IAccountsService accounts,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    ICurrentSession currentSession) : ICommandHandler<ChangePasswordCommand, Result>
{
    public async ValueTask<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

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

        if (change.Status is PasswordChangeStatus.NoSuchAccount)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        if (change.Status is PasswordChangeStatus.IncorrectCurrentPassword)
        {
            return Result.Failure(AccountsErrors.IncorrectCurrentPassword);
        }

        if (change.Status is PasswordChangeStatus.Rejected)
        {
            return Result.Failure(
                AccountsErrors.PasswordChangeRejected(
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
