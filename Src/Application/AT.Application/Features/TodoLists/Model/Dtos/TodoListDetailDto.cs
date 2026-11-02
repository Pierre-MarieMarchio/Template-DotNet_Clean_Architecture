namespace AppTemplate.Application.Features.TodoLists.Model.Dtos;

public sealed record TodoListDetailDto(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt,
    IReadOnlyList<TodoItemDto> Items);
