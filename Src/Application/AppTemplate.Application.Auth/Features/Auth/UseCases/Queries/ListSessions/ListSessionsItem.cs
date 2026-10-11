namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Queries.ListSessions;

/// <summary>One of the caller's live sessions.</summary>
/// <param name="Current">
/// Whether the request was made from this session. False on every item when the caller's access
/// token predates the <c>sid</c> claim: its session is then not known, not absent.
/// </param>
public sealed record ListSessionsItem(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    string? UserAgent,
    bool Current);
