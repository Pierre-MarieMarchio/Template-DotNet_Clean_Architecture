namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.ListSessions;

public sealed record ListSessionsItem(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    string? UserAgent,
    bool Current);
