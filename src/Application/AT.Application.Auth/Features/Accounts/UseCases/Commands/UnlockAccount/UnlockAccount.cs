using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.UnlockAccount;

public sealed record UnlockAccountCommand(Guid UserId) : ICommand<Result>;

public sealed class UnlockAccountCommandHandler(
    IAccountAdministrationService administration,
    ISecurityEventLog securityEventLog) : ICommandHandler<UnlockAccountCommand, Result>
{
    public async ValueTask<Result> Handle(UnlockAccountCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcome = await administration.UnlockAsync(request.UserId, cancellationToken);

        if (outcome.Status is not AccountAdministrationStatus.Applied)
        {
            return Result.Failure(ToError(outcome.Status));
        }

        await securityEventLog.RecordAsync(SecurityEvent.AccountUnlockedByAdministrator(request.UserId));

        return Result.Success();
    }

    private static Error ToError(AccountAdministrationStatus status) => status switch
    {
        AccountAdministrationStatus.NoSuchAccount => AccountsErrors.NoSuchAccount,
        _ => AccountsErrors.AccountLockoutRejected,
    };
}
