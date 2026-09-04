using AppTemplate.Domain.Features.TodoLists.Entities;

namespace AppTemplate.Domain.Features.TodoLists.Repositories;

/// <summary>
/// Deliberately not generic: a repository that fits every entity can only offer CRUD, which is
/// what an aggregate exists to hide. One method per thing a use case actually needs.
/// <para>
/// Nothing here writes: <c>Add</c> and <c>Remove</c> stage, and <c>IUnitOfWork</c> is what commits.
/// </para>
/// </summary>
public interface ITodoListRepository
{
    /// <summary>
    /// Must load the list, its items and their tags. Eager-loading the whole aggregate is a
    /// correctness requirement, not an optimisation: the invariants (unique titles, item cap) can
    /// only be checked against all the items.
    /// </summary>
    /// <returns>The aggregate, or <c>null</c> when no list has that id.</returns>
    Task<TodoList?> GetAsync(Guid id, CancellationToken cancellationToken);

    void Add(TodoList todoList);

    /// <summary>Removes the list and everything it owns.</summary>
    void Remove(TodoList todoList);
}
