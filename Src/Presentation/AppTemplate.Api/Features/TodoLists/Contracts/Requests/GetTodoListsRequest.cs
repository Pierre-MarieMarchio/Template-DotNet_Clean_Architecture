namespace AppTemplate.Api.Features.TodoLists.Contracts.Requests;

/// <summary>The paging, sorting and filters of a todo-list listing.</summary>
public sealed record GetTodoListsRequest(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort,
    string? Search,
    string? CreatedAfter,
    string? CreatedBefore);
