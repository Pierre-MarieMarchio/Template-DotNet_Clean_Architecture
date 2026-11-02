using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;

namespace AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Rows.Models;

internal sealed class SecurityEventRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid? UserId { get; set; }

    public required SecurityEventKind Kind { get; set; }

    public required DateTimeOffset OccurredAt { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? Detail { get; set; }
}
