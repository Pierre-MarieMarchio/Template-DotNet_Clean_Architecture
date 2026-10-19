namespace AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

/// <summary>An issued access token and the instant it stops being accepted.</summary>
public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);
