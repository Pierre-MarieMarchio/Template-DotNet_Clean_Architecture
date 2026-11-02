using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;

public sealed record SecurityEventEntry(
    Guid Id,
    SecurityEventKind Kind,
    DateTimeOffset OccurredAt,
    string? IpAddress,
    string? UserAgent,
    string? Detail);
