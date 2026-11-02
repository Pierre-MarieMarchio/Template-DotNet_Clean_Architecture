namespace AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;

public interface ISecurityEventLog
{
    void Record(SecurityEvent securityEvent);

    Task<int> PurgeExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
