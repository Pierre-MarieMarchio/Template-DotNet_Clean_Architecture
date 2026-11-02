using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTodoItem;

public sealed record RemoveTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    VersionPrecondition? Precondition = null);
