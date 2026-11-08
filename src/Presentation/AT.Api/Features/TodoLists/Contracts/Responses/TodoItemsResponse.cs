namespace AppTemplate.Api.Features.TodoLists.Contracts.Responses;

/// <summary>The items of one todo list.</summary>
/// <param name="Items">Ordered by title.</param>
public sealed record TodoItemsResponse(IReadOnlyList<TodoItemResponse> Items);
