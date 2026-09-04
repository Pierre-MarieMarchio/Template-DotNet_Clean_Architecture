namespace AppTemplate.Application.Features.TodoLists.Dtos;

/// <param name="Title">Unique within its list.</param>
/// <param name="Tags">Normalised to lower case.</param>
public sealed record TodoItemDto(
    Guid Id,
    string Title,
    string? Description,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<string> Tags);
