using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTagFromTodoItem;

public interface IRemoveTagFromTodoItemUseCase
    : IUseCase<RemoveTagFromTodoItemCommand, Result<Versioned<TodoItemDto>>>;
