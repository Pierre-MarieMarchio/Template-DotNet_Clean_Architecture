namespace AppTemplate.Api.Features.TodoLists.Contracts.Responses;

/// <summary>
/// The wire shape of one item, and the body of every write that addresses one: a caller never has to
/// re-read what it just changed.
/// </summary>
/// <param name="Title">Unique within its list.</param>
/// <param name="Tags">Normalised to lower case.</param>
public sealed record TodoItemResponse(
    Guid Id,
    string Title,
    string? Description,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<string> Tags);
