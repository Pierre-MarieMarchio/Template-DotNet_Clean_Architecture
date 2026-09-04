using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.CompleteTodoItem;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional completion.
/// </param>
public sealed record CompleteTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    VersionPrecondition? Precondition = null);
