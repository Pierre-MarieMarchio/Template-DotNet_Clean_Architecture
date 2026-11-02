using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Models;

internal sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string TokenHash { get; set; }

    public required Guid UserId { get; set; }

    public required Guid FamilyId { get; set; }

    public RefreshTokenFamilyRecord? Family { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    public AppUser? User { get; set; }

    public bool IsActiveAt(DateTimeOffset instant) => RevokedAt is null && ExpiresAt > instant;
}
