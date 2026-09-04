namespace AppTemplate.Application.Features.Auth.Dtos;

/// <summary>
/// <paramref name="RefreshToken"/> is the raw secret, returned once and never persisted in this
/// form.
/// </summary>
public sealed record LoginResponse(
    Guid UserId,
    string UserName,
    string Email,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
