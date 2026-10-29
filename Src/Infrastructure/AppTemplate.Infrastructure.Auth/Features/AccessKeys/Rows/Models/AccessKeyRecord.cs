namespace AppTemplate.Infrastructure.Auth.Features.AccessKeys.Rows.Models;

internal sealed class AccessKeyRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required Guid UserId { get; set; }

    public required string Name { get; set; }

    public required string KeyHash { get; set; }

    public required string Prefix { get; set; }

    public required string Scopes { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
