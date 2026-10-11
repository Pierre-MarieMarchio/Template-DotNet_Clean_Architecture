namespace AppTemplate.Infrastructure.Auth.Features.Auth.Tables;

/// <summary>A stored refresh-token grant, as the identity module is allowed to see it.</summary>
/// <param name="UserId">The account the grant belongs to.</param>
/// <param name="FamilyId">The session the grant belongs to: the id every successor of this grant shares.</param>
/// <param name="ExpiresAt">When the grant stops being accepted.</param>
/// <param name="RevokedAt">When it was revoked or rotated, or <c>null</c> while it is live.</param>
/// <param name="FamilyRevokedAt">When the grant's session was ended, or <c>null</c> while it runs.
/// A live grant with a revoked family is one a rotation was issuing when the revocation committed:
/// dead on arrival, and not evidence of anything.</param>
public sealed record RefreshTokenGrant(
    Guid UserId,
    Guid FamilyId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? FamilyRevokedAt)
{
    /// <summary>Expiry is compared explicitly on every presentation, so a grant past
    /// <see cref="ExpiresAt"/> is rejected immediately, without depending on a background job to
    /// have marked it inactive first.</summary>
    public bool IsActiveAt(DateTimeOffset instant) => RevokedAt is null && ExpiresAt > instant;
}
