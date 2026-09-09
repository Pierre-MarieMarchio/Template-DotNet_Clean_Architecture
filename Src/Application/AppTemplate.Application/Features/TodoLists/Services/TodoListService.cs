using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ownership;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;

namespace AppTemplate.Application.Features.TodoLists.Services;


internal sealed class TodoListService(ITodoListRepository repository, ICurrentUser currentUser) : ITodoListService
{
    public async Task<Result<TodoList>> LoadOwnedAsync(
        Guid todoListId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<TodoList>();
        }

        return OwnedAggregate.Require(
            await repository.GetAsync(todoListId, cancellationToken),
            userId.Value,
            TodoListErrors.ListNotFound(todoListId),
            precondition);
    }
}
