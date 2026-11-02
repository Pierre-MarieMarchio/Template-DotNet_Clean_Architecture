using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Commands.PurgeExpiredSecurityEvents;

public sealed record PurgeExpiredSecurityEventsCommand : ICommand<Result<int>>;

public sealed class PurgeExpiredSecurityEventsCommandHandler(
    ISecurityEventLog securityEventLog,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<PurgeExpiredSecurityEventsCommand, Result<int>>
{
    public async ValueTask<Result<int>> Handle(PurgeExpiredSecurityEventsCommand command, CancellationToken cancellationToken) =>
        await securityEventLog.PurgeExpiredAsync(dateTimeProvider.UtcNow, cancellationToken);
}
