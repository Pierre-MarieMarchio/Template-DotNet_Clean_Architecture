using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTagToTodoItem;

public interface IAddTagToTodoItemUseCase : IUseCase<AddTagToTodoItemCommand, Result<Versioned<TodoItemDto>>>;
