using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;

/// <summary>
/// The owner filter comes from <see cref="ICurrentUser"/> and is deliberately not part of the
/// query, so no caller can widen it.
/// </summary>
public interface IGetTodoListsUseCase : IUseCase<GetTodoListsQuery, Result<PagedResult<TodoListSummaryDto>>>;
