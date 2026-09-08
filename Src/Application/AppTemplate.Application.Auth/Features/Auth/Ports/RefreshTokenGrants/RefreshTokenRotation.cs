namespace AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenGrants;

/// <summary>
/// The result of presenting a refresh token. <see cref="Succeeded"/> is false for an unknown,
/// expired, revoked or replayed token — the caller must not be told which.
/// </summary>
public sealed record RefreshTokenRotation(bool Succeeded, Guid? UserId, IssuedRefreshToken? Token)
{
    /// <summary>The presentation bought nothing: no successor, and no account named back.</summary>
    public static RefreshTokenRotation Rejected { get; } = new(false, null, null);

    /// <summary>The presented token is now spent and <paramref name="token"/> is its one successor.</summary>
    public static RefreshTokenRotation Rotated(Guid userId, IssuedRefreshToken token) =>
        new(true, userId, token);
}
