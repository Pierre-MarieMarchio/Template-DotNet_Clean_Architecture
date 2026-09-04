namespace AppTemplate.Application.Features.Auth.Dtos;

public sealed record RefreshAccessTokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
