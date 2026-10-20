namespace AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);
