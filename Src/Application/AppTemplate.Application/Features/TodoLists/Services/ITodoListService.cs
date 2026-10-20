using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;

namespace AppTemplate.Application.Features.TodoLists.Services;

public interface ITodoListService
{
    Task<Result<TodoList>> LoadOwnedAsync(
        Guid todoListId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);
}
