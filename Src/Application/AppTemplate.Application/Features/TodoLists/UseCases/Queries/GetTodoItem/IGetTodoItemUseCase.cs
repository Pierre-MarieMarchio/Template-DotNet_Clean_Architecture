using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItem;

public interface IGetTodoItemUseCase : IUseCase<GetTodoItemQuery, Result<Versioned<TodoItemDto>>>;
