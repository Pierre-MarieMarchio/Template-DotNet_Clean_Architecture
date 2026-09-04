using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Domain.Features.TodoLists.Entities;

namespace AppTemplate.Application.Features.TodoLists.Services;

/// <summary>
/// The one gate every to-do list command loads its aggregate through: identity, ownership and
/// the version precondition, in that order, so every use case rejects the same way for the same
/// reasons instead of repeating the three checks slightly differently each time.
/// </summary>
public interface ITodoListAccess
{
    /// <returns>
    /// The aggregate, or a failure — <see cref="TodoListErrors.ListNotFound"/> for an anonymous
    /// caller, an unknown id or somebody else's list, and
    /// <see cref="ConcurrencyErrors.PreconditionFailed"/> once ownership is established but the
    /// caller named a version the aggregate no longer holds.
    /// </returns>
    Task<Result<TodoList>> LoadOwnedAsync(
        Guid todoListId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);
}
