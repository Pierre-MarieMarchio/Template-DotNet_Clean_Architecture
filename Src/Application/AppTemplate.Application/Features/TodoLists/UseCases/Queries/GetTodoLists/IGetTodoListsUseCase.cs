using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;

public interface IGetTodoListsUseCase : IUseCase<GetTodoListsQuery, Result<PagedResult<TodoListSummaryDto>>>;
