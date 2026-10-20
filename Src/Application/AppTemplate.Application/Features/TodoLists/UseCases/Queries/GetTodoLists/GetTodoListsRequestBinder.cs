using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Policies;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;

public static class GetTodoListsRequestBinder
{
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
