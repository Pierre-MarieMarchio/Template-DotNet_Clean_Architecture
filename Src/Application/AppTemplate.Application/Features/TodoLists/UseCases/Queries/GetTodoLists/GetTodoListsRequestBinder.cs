using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Policies;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;

/// <summary>
/// Turns a <see cref="GetTodoListsQuery"/>'s raw strings into the shape
/// <see cref="ITodoListQueries"/> accepts. Kept apart from the use case because none of this is
/// application logic — it is query-string translation.
/// </summary>
/// <remarks>
/// <see cref="CollectionBinding"/> does the half of that translation that is the same for every
/// collection. What is written out here is what only this feature knows: what a caller may filter
/// its lists by, and which of its sortable fields holds an instant rather than a string.
/// </remarks>
public static class GetTodoListsRequestBinder
{
    /// <summary>Binds one page of to-do lists, or answers the caller's first mistake.</summary>
    /// <param name="query">The raw query values.</param>
    /// <returns>The bound request, or the refusal.</returns>
    public static Result<FeaturePageRequest<TodoListFilter>> Bind(GetTodoListsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return CollectionBinding.Bind(
            query,
            TodoListCollectionPolicy.Instance,
            () => TodoListFilter.Create(query.Search, query.CreatedAfter, query.CreatedBefore),
            TodoListCollectionPolicy.CreatedAtField);
    }
}
