using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReopenTodoItem;

public interface IReopenTodoItemUseCase : IUseCase<ReopenTodoItemCommand, Result<Versioned<TodoItemDto>>>;
