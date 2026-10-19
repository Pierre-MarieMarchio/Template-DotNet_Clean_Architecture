namespace AppTemplate.Application.Features.TodoLists.Model.Dtos;

/// <param name="Items">Ordered by title.</param>
public sealed record TodoListDetailDto(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt,
    IReadOnlyList<TodoItemDto> Items);
