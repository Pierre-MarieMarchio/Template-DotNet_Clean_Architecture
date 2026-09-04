namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>
/// One token pair, defined once and served by both sign-in and refresh: a client parses tokens the
/// same way whichever endpoint minted them.
/// </summary>
/// <param name="RefreshToken">The raw secret, returned once and never persisted in this form.</param>
public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
