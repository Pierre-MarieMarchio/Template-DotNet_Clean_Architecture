namespace AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;

/// <summary>A refresh token as the client sees it: the raw secret plus its expiry.</summary>
public sealed record IssuedRefreshToken(string Value, DateTimeOffset ExpiresAt);
