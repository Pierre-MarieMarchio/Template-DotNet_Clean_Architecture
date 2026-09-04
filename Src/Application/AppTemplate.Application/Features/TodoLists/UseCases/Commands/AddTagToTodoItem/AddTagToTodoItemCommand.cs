using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTagToTodoItem;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional add.
/// </param>
public sealed record AddTagToTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    string Tag,
    VersionPrecondition? Precondition = null);
