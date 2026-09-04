using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItem;

public interface IGetTodoItemUseCase : IUseCase<GetTodoItemQuery, Result<Versioned<TodoItemDto>>>;
