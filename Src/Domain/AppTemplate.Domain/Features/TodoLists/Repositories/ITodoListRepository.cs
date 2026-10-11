using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Entities;

namespace AppTemplate.Domain.Features.TodoLists.Repositories;

/// <summary>
/// Deliberately not generic: a repository that fits every entity can only offer CRUD, which is
/// what an aggregate exists to hide; one method per thing a use case actually needs. Nothing here
/// writes: <c>Add</c> and <c>Remove</c> stage, and <c>IUnitOfWork</c> is what commits.
/// </summary>
public interface ITodoListRepository
{
    /// <summary>Must load the list, its items and their tags — a correctness requirement, not an
    /// optimisation, since the invariants can only be checked against all the items.</summary>
    /// <returns>The aggregate, or <c>null</c> when no list has that id.</returns>
    Task<TodoList?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Every list one owner holds, as aggregates: what is done with them next — removal, on an
    /// account-deleted policy — goes through the aggregate, not around it.
    /// </summary>
    Task<IReadOnlyList<TodoList>> GetForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken);

    void Add(TodoList todoList);

    /// <summary>Removes the list and everything it owns.</summary>
    void Remove(TodoList todoList);
}
