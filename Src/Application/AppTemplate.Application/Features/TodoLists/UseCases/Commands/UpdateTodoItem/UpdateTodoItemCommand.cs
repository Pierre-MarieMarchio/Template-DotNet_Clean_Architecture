using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.UpdateTodoItem;

public sealed record UpdateTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    string Title,
    string? Description,
    VersionPrecondition? Precondition = null);
