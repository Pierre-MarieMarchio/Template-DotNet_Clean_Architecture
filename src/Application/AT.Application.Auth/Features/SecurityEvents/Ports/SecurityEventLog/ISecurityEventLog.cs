namespace AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;

public interface ISecurityEventLog
{
    Task RecordAsync(SecurityEvent securityEvent);

    Task<int> PurgeExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
