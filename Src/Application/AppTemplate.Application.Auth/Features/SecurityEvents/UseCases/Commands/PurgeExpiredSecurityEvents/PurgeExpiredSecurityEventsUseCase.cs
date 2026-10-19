using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Commands.PurgeExpiredSecurityEvents;

/// <summary>
/// Deletes every security event older than the retention period. The trail is kept for that period
/// and no longer — also for an account since deleted, whose events are removed here and nowhere else.
/// No leader lease, like <c>PurgeExpiredRefreshTokensUseCase</c>: the delete is idempotent, so two
/// hosts running it at once remove each row once between them.
/// </summary>
public sealed class PurgeExpiredSecurityEventsUseCase(
    ISecurityEventLog securityEventLog,
    IDateTimeProvider dateTimeProvider) : IPurgeExpiredSecurityEventsUseCase
{
    /// <inheritdoc />
    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        await securityEventLog.PurgeExpiredAsync(dateTimeProvider.UtcNow, cancellationToken);
}
