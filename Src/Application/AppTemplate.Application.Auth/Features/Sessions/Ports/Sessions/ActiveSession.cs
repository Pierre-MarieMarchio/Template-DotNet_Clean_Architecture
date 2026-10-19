namespace AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;

/// <summary>One of an account's live sessions, as its owner sees it in a list.</summary>
/// <param name="Id">The session's id: the <c>sid</c> claim of the access tokens issued in it.</param>
/// <param name="CreatedAt">When the sign-in that started it happened.</param>
/// <param name="LastUsedAt">When its refresh token was last rotated, or <see cref="CreatedAt"/> when it
/// never was.</param>
/// <param name="UserAgent">The client's <c>User-Agent</c> at its last use, shortened, or <c>null</c>.</param>
public sealed record ActiveSession(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, string? UserAgent);
