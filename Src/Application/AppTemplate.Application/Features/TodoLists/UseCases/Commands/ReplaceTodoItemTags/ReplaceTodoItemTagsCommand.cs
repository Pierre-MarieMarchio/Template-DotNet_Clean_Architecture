using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReplaceTodoItemTags;

/// <param name="Tags">The complete set the item should end up with; anything not in it is removed.</param>
/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional replacement.
/// </param>
public sealed record ReplaceTodoItemTagsCommand(
    Guid TodoListId,
    Guid TodoItemId,
    IReadOnlyList<string> Tags,
    VersionPrecondition? Precondition = null);
