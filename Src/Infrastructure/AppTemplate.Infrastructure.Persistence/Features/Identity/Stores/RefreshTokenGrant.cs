namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Stores;

/// <summary>A stored refresh-token grant, as the identity module is allowed to see it.</summary>
/// <param name="UserId">The account the grant belongs to.</param>
/// <param name="ExpiresAt">When the grant stops being accepted.</param>
/// <param name="RevokedAt">When it was revoked or rotated, or <c>null</c> while it is live.</param>
public sealed record RefreshTokenGrant(Guid UserId, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt)
{
    /// <summary>Expiry is compared explicitly on every presentation; it used to be stored and ignored.</summary>
    public bool IsActiveAt(DateTimeOffset instant) => RevokedAt is null && ExpiresAt > instant;
}
