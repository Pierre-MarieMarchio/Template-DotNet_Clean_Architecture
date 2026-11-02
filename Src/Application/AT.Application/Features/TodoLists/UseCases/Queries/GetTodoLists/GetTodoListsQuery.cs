using AppTemplate.Application.Core.Common.Collections;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;

public sealed record GetTodoListsQuery(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort,
    string? Search,
    string? CreatedAfter,
    string? CreatedBefore) : ICollectionQuery
{
    public static GetTodoListsQuery Offset(int? page, int? pageSize) =>
        new(null, page, pageSize, null, null, null, null, null);
}
