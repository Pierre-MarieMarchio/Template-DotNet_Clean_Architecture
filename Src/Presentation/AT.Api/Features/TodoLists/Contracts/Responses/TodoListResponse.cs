namespace AppTemplate.Api.Features.TodoLists.Contracts.Responses;

/// <summary>One todo list with all its items.</summary>
/// <param name="Items">Ordered by title.</param>
public sealed record TodoListResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt,
    IReadOnlyList<TodoItemResponse> Items);
