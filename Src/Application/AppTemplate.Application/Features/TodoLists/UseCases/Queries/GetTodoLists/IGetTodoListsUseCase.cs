using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;

/// <summary>
/// The owner filter comes from <see cref="ICurrentUser"/> and is deliberately not part of the
/// query, so no caller can widen it.
/// </summary>
public interface IGetTodoListsUseCase : IUseCase<GetTodoListsQuery, Result<PagedResult<TodoListSummaryDto>>>;
