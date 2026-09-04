using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;

public interface IGetTodoListUseCase : IUseCase<GetTodoListQuery, Result<Versioned<TodoListDetailDto>>>;
