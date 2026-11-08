namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Access.Tables;

public sealed record RefreshTokenGrant(
    Guid UserId,
    Guid FamilyId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? FamilyRevokedAt)
{
    public bool IsActiveAt(DateTimeOffset instant) => RevokedAt is null && ExpiresAt > instant;
}
