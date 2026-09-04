using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional rename.
/// </param>
public sealed record RenameTodoListCommand(
    Guid TodoListId,
    string Name,
    VersionPrecondition? Precondition = null);
