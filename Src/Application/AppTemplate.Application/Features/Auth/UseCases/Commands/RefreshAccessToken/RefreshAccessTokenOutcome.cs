namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken;

public sealed record RefreshAccessTokenOutcome(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
