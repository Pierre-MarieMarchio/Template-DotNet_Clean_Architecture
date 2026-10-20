namespace AppTemplate.Api.Features.TodoLists.Contracts.Responses;

/// <summary>One item, also the body of every write that addresses it.</summary>
/// <param name="Title">Unique within its list.</param>
/// <param name="Tags">Normalised to lower case.</param>
public sealed record TodoItemResponse(
    Guid Id,
    string Title,
    string? Description,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<string> Tags);
