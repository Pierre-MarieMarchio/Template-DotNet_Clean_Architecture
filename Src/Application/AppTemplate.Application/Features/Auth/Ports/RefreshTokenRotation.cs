namespace AppTemplate.Application.Features.Auth.Ports;

/// <summary>
/// The result of presenting a refresh token. <see cref="Succeeded"/> is false for an unknown,
/// expired, revoked or replayed token — the caller must not be told which.
/// </summary>
public sealed record RefreshTokenRotation(bool Succeeded, Guid? UserId, IssuedRefreshToken? Token)
{
    public static RefreshTokenRotation Rejected { get; } = new(false, null, null);

    public static RefreshTokenRotation Rotated(Guid userId, IssuedRefreshToken token) =>
        new(true, userId, token);
}
