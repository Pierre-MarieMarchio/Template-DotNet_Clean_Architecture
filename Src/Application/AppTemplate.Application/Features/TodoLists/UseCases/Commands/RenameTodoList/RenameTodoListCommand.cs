using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;

public sealed record RenameTodoListCommand(
    Guid TodoListId,
    string Name,
    VersionPrecondition? Precondition = null);
