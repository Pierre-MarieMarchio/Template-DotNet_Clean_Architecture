namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken;

public sealed record RefreshAccessTokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
