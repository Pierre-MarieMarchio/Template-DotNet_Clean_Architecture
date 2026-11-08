namespace AppTemplate.Application.Features.TodoLists.Model.Dtos;

public sealed record TodoItemDto(
    Guid Id,
    string Title,
    string? Description,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<string> Tags);
