using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.UpdateTodoItem;

/// <param name="Title">Must stay unique within the list, excluding the item itself.</param>
/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional update.
/// </param>
public sealed record UpdateTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    string Title,
    string? Description,
    VersionPrecondition? Precondition = null);
