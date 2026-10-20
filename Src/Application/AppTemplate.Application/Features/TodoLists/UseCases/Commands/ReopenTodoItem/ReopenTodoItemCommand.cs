using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReopenTodoItem;

public sealed record ReopenTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    VersionPrecondition? Precondition = null);
