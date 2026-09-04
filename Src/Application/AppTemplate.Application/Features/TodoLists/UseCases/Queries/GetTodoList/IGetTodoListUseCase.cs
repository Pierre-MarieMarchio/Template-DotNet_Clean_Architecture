using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;

public interface IGetTodoListUseCase : IUseCase<GetTodoListQuery, Result<Versioned<TodoListDetailDto>>>;
