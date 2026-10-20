namespace AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

public sealed record IssuedRefreshToken(string Value, DateTimeOffset ExpiresAt, Guid SessionId);
