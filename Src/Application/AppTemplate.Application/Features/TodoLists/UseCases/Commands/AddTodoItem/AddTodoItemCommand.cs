using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;

/// <param name="Title">Must be unique within the list.</param>
/// <param name="Tags">Normalised and de-duplicated by the domain.</param>
/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional add.
/// </param>
public sealed record AddTodoItemCommand(
    Guid TodoListId,
    string Title,
    string? Description,
    IReadOnlyList<string>? Tags,
    VersionPrecondition? Precondition = null);
