using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;

public sealed record AddTodoItemCommand(
    Guid TodoListId,
    string Title,
    string? Description,
    IReadOnlyList<string>? Tags,
    VersionPrecondition? Precondition = null);
