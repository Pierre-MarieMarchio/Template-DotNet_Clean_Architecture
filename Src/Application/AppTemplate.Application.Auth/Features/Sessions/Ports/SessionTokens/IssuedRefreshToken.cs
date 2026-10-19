namespace AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

/// <summary>A refresh token as the client sees it: the raw secret plus its expiry.</summary>
/// <param name="SessionId">The session the token belongs to: every successor rotated from it carries
/// the same id, and the access tokens issued beside it name it in their <c>sid</c> claim.</param>
public sealed record IssuedRefreshToken(string Value, DateTimeOffset ExpiresAt, Guid SessionId);
