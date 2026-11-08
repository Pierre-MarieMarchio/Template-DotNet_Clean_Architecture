namespace AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Rows.Models;

internal sealed class ExternalIdentityRecord
{
    public Guid Id { get; set; }

    public string Issuer { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
