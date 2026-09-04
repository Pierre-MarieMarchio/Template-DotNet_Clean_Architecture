namespace AppTemplate.Api.Features.TodoLists.Contracts.Responses;

/// <summary>The wire shape of one todo list with its items: the aggregate's full representation.</summary>
/// <param name="Items">Ordered by title.</param>
public sealed record TodoListResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt,
    IReadOnlyList<TodoItemResponse> Items);
