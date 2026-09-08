namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RefreshAccessToken;

/// <summary>Both halves of a renewed session: the token pair replacing the one presented.</summary>
/// <param name="RefreshToken">
/// The successor grant. The presented one is spent by now, so a client that fails to store this
/// has no way back other than signing in again.
/// </param>
public sealed record RefreshAccessTokenOutcome(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
