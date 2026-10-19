using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Models;

/// <summary>
/// One row of the security-event trail. <see cref="UserId"/> carries no foreign key on purpose: the
/// row outlives the account it names until the retention purge removes it, and an attempt against an
/// address that named no account has no user at all.
/// </summary>
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
