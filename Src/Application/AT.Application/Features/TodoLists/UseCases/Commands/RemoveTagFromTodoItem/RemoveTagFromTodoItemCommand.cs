using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTagFromTodoItem;

public sealed record RemoveTagFromTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    string Tag,
    VersionPrecondition? Precondition = null);
