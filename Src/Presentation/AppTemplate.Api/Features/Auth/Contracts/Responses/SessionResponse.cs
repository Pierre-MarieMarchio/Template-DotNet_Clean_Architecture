namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>One of the caller's live sessions, as the session list shows it.</summary>
/// <param name="Id">The session's id: what the revoke endpoint takes to end it.</param>
/// <param name="CreatedAt">When the sign-in that started it happened.</param>
/// <param name="LastUsedAt">
/// When its refresh token was last rotated, or <see cref="CreatedAt"/> when it never was.
/// </param>
/// <param name="UserAgent">
/// The client's <c>User-Agent</c> at its last use, shortened, or <c>null</c> when it said nothing.
/// </param>
/// <param name="Current">Whether the request was made from this session.</param>
public sealed record SessionResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    string? UserAgent,
    bool Current);

