using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReplaceTodoItemTags;

public sealed record ReplaceTodoItemTagsCommand(
    Guid TodoListId,
    Guid TodoItemId,
    IReadOnlyList<string> Tags,
    VersionPrecondition? Precondition = null);
