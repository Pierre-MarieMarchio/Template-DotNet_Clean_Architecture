using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;

public sealed record DeleteTodoListCommand(Guid TodoListId, VersionPrecondition? Precondition = null);
