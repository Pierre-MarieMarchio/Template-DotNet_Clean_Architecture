namespace AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;

/// <summary>A refresh token as the client sees it: the raw secret plus its expiry.</summary>
public sealed record IssuedRefreshToken(string Value, DateTimeOffset ExpiresAt);
