using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Policies;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;

/// <summary>
/// Turns a <see cref="GetTodoListsQuery"/>'s raw strings into a <see cref="TodoListPageRequest"/>,
/// applying <see cref="TodoListCollectionPolicy.Instance"/>'s whitelist along the way. Kept apart
/// from the use case because none of this is application logic — it is query-string translation,
/// and the next paginated collection this feature grows will need its own copy of exactly this
/// shape rather than of the use case around it.
/// </summary>
/// <remarks>
/// <see cref="CollectionOrder"/> does the half of that translation that is the same for every
/// collection. What is written out here is what only this feature knows: what a caller may filter
/// its lists by, and which of its sortable fields holds an instant rather than a string.
/// </remarks>
public static class GetTodoListsRequestBinder
{
    public static Result<TodoListPageRequest> Bind(GetTodoListsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var policy = TodoListCollectionPolicy.Instance;

        var orderResult = CollectionOrder.Parse(query.Paging, query.Sort, policy);

        if (orderResult.IsFailure)
        {
            return orderResult.To<TodoListPageRequest>();
        }

        var order = orderResult.Value;

        var filterResult = TodoListFilter.Create(query.Search, query.CreatedAfter, query.CreatedBefore);

        if (filterResult.IsFailure)
        {
            return filterResult.To<TodoListPageRequest>();
        }

        var pagingResult = order.ToPageRequest(
            query.Page,
            query.PageSize,
            query.Cursor,
            TodoListCollectionPolicy.CreatedAtField);

        if (pagingResult.IsFailure)
        {
            return pagingResult.To<TodoListPageRequest>();
        }

        return TodoListPageRequest.Of(pagingResult.Value, order.Sort, filterResult.Value);
    }
}
