using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Models;

internal sealed class RefreshTokenFamilyRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required Guid UserId { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset LastUsedAt { get; set; }

    public string? UserAgent { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public AppUser? User { get; set; }

    public ICollection<RefreshToken> Tokens { get; } = [];
}
