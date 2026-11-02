using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTagToTodoItem;

public sealed record AddTagToTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    string Tag,
    VersionPrecondition? Precondition = null);
