using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItems;

/// <summary>
/// No pagination: the aggregate is bounded by <c>TodoList.MaxItems</c>, and
/// <see cref="ITodoListQueries.GetDetailAsync"/> already returns every item, so this is a
/// reshaping of that same call rather than a collection of its own.
/// </summary>
public interface IGetTodoItemsUseCase : IUseCase<GetTodoItemsQuery, Result<Versioned<IReadOnlyList<TodoItemDto>>>>;
