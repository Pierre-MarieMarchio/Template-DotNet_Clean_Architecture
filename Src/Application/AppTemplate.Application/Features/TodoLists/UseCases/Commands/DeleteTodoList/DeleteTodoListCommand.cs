using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;

/// <summary>The list, and its items and tags with it.</summary>
/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional delete.
/// </param>
public sealed record DeleteTodoListCommand(Guid TodoListId, VersionPrecondition? Precondition = null);
