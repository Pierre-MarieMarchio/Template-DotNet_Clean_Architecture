using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTagFromTodoItem;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional removal.
/// </param>
public sealed record RemoveTagFromTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    string Tag,
    VersionPrecondition? Precondition = null);
