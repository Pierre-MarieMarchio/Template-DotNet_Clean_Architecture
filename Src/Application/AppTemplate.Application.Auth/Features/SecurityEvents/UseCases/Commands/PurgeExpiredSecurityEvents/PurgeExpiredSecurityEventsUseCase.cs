using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Commands.PurgeExpiredSecurityEvents;

public sealed class PurgeExpiredSecurityEventsUseCase(
    ISecurityEventLog securityEventLog,
    IDateTimeProvider dateTimeProvider) : IPurgeExpiredSecurityEventsUseCase
{
    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        await securityEventLog.PurgeExpiredAsync(dateTimeProvider.UtcNow, cancellationToken);
}
