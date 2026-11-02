//#if (browserSessions)
using System.Text.Json.Serialization;

//#endif
namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>One token pair, in the same shape from sign-in and from refresh.</summary>
/// <param name="RefreshToken">The raw secret, returned once and never persisted in this form.</param>
public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    //#if (browserSessions)
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    //#endif
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
