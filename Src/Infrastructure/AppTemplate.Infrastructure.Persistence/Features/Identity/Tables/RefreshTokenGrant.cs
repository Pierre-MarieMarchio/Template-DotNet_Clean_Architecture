namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Tables;

/// <summary>A stored refresh-token grant, as the identity module is allowed to see it.</summary>
/// <param name="UserId">The account the grant belongs to.</param>
/// <param name="ExpiresAt">When the grant stops being accepted.</param>
/// <param name="RevokedAt">When it was revoked or rotated, or <c>null</c> while it is live.</param>
public sealed record RefreshTokenGrant(Guid UserId, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt)
{
    /// <summary>Expiry is compared explicitly on every presentation, so a grant past
    /// <see cref="ExpiresAt"/> is rejected immediately, without depending on a background job to
    /// have marked it inactive first.</summary>
    public bool IsActiveAt(DateTimeOffset instant) => RevokedAt is null && ExpiresAt > instant;
}
