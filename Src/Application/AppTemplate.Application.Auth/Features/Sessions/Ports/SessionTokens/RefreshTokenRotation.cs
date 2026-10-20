namespace AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

public sealed record RefreshTokenRotation(bool Succeeded, Guid? UserId, IssuedRefreshToken? Token)
{
    public static RefreshTokenRotation Rejected { get; } = new(false, null, null);

    public static RefreshTokenRotation Rotated(Guid userId, IssuedRefreshToken token) =>
        new(true, userId, token);
}
