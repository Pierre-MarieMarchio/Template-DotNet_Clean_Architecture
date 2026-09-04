using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTagFromTodoItem;

public interface IRemoveTagFromTodoItemUseCase
    : IUseCase<RemoveTagFromTodoItemCommand, Result<Versioned<TodoItemDto>>>;
