namespace AppTemplate.Api.Features.TodoLists.Contracts.Responses;

/// <summary>
/// The items of one todo list, wrapped in an object: an array at the top level can never gain a
/// sibling field without breaking its callers, which is the same reason nothing here answers with a
/// bare scalar either.
/// </summary>
/// <remarks>
/// Not paginated. The aggregate is bounded by <c>TodoList.MaxItems</c>, so the whole set always fits
/// in one response and no page could ever be a partial answer.
/// </remarks>
/// <param name="Items">Ordered by title.</param>
public sealed record TodoItemsResponse(IReadOnlyList<TodoItemResponse> Items);
