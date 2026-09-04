namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken;

/// <summary>The presented token is always consumed, success or failure.</summary>
public sealed record RefreshAccessTokenCommand(string RefreshToken);
