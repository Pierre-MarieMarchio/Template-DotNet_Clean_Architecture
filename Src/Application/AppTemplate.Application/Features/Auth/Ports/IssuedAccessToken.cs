namespace AppTemplate.Application.Features.Auth.Ports;

/// <summary>An issued access token and the instant it stops being accepted.</summary>
public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);
