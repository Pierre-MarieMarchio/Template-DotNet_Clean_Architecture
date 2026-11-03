using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.TwoFactor.Model.Errors;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.DisableTwoFactor;

public sealed record DisableTwoFactorCommand(string CurrentPassword) : ICommand<Result>;

public sealed class DisableTwoFactorCommandHandler(
    ITwoFactorService twoFactor,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    ICurrentSession currentSession) : ICommandHandler<DisableTwoFactorCommand, Result>
{
    public async ValueTask<Result> Handle(DisableTwoFactorCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId;
        }

        var disabled = await twoFactor.DisableAsync(userId.Value.Value, request.CurrentPassword, cancellationToken);

        if (disabled is TwoFactorDisableStatus.NoSuchAccount)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        if (disabled is TwoFactorDisableStatus.IncorrectPassword)
        {
            return Result.Failure(AccountsErrors.IncorrectCurrentPassword);
        }

        if (disabled is TwoFactorDisableStatus.Rejected)
        {
            return Result.Failure(TwoFactorErrors.TwoFactorDisableRejected);
        }

        await securityEventLog.RecordAsync(SecurityEvent.TwoFactorDisabled(userId.Value.Value));

        await CredentialInvalidationPolicy.InvalidateOtherSessionsAsync(
            sessionTokens,
            securityEventLog,
            userId.Value.Value,
            currentSession.SessionId,
            cancellationToken);

        return Result.Success();
    }
}
