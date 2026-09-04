using AppTemplate.Domain.Features.TodoLists.Entities;

namespace AppTemplate.Domain.Features.TodoLists.Stores;

/// <summary>
/// Deliberately not generic: a repository that fits every entity can only offer CRUD, which is
/// what an aggregate exists to hide. One method per thing a use case actually needs.
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

    /// <summary>Stages a new aggregate for insertion.</summary>
    void Add(TodoList todoList);

    /// <summary>Stages an aggregate for deletion, together with everything it owns.</summary>
    void Remove(TodoList todoList);
}
