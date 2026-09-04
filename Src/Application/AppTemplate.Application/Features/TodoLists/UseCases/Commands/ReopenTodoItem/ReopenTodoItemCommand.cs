using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReopenTodoItem;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional reopen.
/// </param>
public sealed record ReopenTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    VersionPrecondition? Precondition = null);
